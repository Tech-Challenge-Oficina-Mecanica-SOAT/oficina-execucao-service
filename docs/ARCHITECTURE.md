# Decisões de arquitetura e descobertas

Achados feitos durante a implementação (incompatibilidades do AWS Academy, versões desatualizadas, etc.), no mesmo espírito do `docs/ARCHITECTURE.md` do `oficina-infra-k8s` e do `oficina-infra-db`.

## Descoberta: SCP nega leitura de object-lock em qualquer bucket S3

**Contexto:** ao rodar `terraform apply` em `infra/bootstrap` pela primeira vez contra uma conta AWS Academy real, a criação do bucket de state (`aws_s3_bucket.terraform_state`) falhou logo depois de criado, na leitura de atributos que o provider AWS faz automaticamente após todo `Create`/`Update`/refresh de um `aws_s3_bucket`:

```
Error: reading S3 Bucket (oficina-execucao-service-terraform-state) object lock configuration:
operation error S3: GetObjectLockConfiguration, https response error StatusCode: 403, ...
api error AccessDenied: ... is not authorized to perform: s3:GetBucketObjectLockConfiguration
on resource: "arn:aws:s3:::oficina-execucao-service-terraform-state"
with an explicit deny in a service control policy: arn:aws:organizations::.../policy/o-.../service_control_policy/p-...
```

A tabela DynamoDB de lock (`aws_dynamodb_table.terraform_lock`) e o próprio bucket já tinham sido criados com sucesso antes do erro — só a leitura pós-criação falhou. Terraform marcou `aws_s3_bucket.terraform_state` como `tainted`.

**Causa raiz:** a conta AWS Academy Learner Lab usada tem uma Service Control Policy org-wide que nega explicitamente `s3:GetBucketObjectLockConfiguration` em qualquer bucket, para qualquer principal. O provider `hashicorp/aws` (recurso `aws_s3_bucket`, testado na v6.64.0) sempre chama essa API como parte de popular os atributos computados do recurso — tanto na criação quanto em qualquer refresh de estado subsequente. Isso significa que **qualquer** `terraform plan`/`apply` que precise atualizar o estado desse recurso (inclusive um `plan` que não muda nada) volta a falhar, não é um erro só do primeiro `apply`.

**Armadilha:** tentar simplesmente rodar `apply` de novo com o recurso `tainted` faz o Terraform destruir e recriar o bucket (já que um recurso tainted é sempre substituído), batendo no mesmo erro de novo na recriação — e dessa vez o bucket real foi apagado no meio do processo antes de falhar de novo na leitura. `-refresh=false` sozinho **não** evita isso quando o recurso já está tainted, porque a leitura acontece como parte do próprio `Create`, não só do refresh inicial.

**Contorno usado (funcionou):**

```bash
cd infra/bootstrap

# 1. Tira a marca de tainted (operação só de estado, não toca na AWS)
terraform untaint aws_s3_bucket.terraform_state

# 2. Confirma que, sem refresh, não sobrou nenhuma mudança pendente pro bucket em si
terraform plan -refresh=false
# Esperado: só os sub-recursos que faltam (versioning/encryption/public-access-block),
# nunca "must be replaced" no aws_s3_bucket

# 3. Aplica sem refresh, evitando a leitura de object-lock do bucket já existente
terraform apply -refresh=false -auto-approve
```

Isso funciona porque `-refresh=false` faz o Terraform confiar no estado já salvo em vez de tentar reconciliar com a AWS antes de planejar — e como o bucket não tinha nenhuma mudança pendente (só precisava não ser tocado), nenhuma chamada a `GetObjectLockConfiguration` acontece.

**Implicação para o resto do grupo:** isso não é específico deste bucket — é uma restrição da conta/organização Academy. Qualquer `aws_s3_bucket` criado por Terraform (aqui ou em outro repositório do grupo, ex. `oficina-infra-db`) pode bater no mesmo problema na primeira aplicação real contra uma conta com essa SCP. Se acontecer:

1. Rode `terraform untaint <endereço-do-recurso>` se ele ficou marcado como tainted.
2. Confirme com `terraform plan -refresh=false` que não sobrou nenhuma mudança pendente para esse recurso.
3. Aplique com `terraform apply -refresh=false`.

Rodar `terraform destroy` desse bucket depois também vai precisar de `-refresh=false` pelo mesmo motivo (o destroy também tenta ler o estado atual antes de apagar).

## Descoberta: sessões da AWS Academy são efêmeras — e o nome do bucket de state não é

**Contexto:** cada sessão de lab da AWS Academy é uma conta nova (nada persiste entre sessões — é assim que o grupo sempre gravou os vídeos de entrega, setup do zero a cada gravação). Ao reaplicar `infra/bootstrap` numa sessão nova em 09/11, nada do que existia na sessão de setembro estava lá: nem o bucket de state, nem a tabela de lock, nem a tabela DynamoDB de dados do serviço, nem o cluster EKS do `oficina-infra-k8s`. Esperado.

**O problema:** nomes de bucket S3 são únicos **globalmente**, não por conta. O bucket `oficina-execucao-service-terraform-state` da sessão de setembro continua reservando esse nome mesmo com a conta daquela sessão inacessível — `terraform apply` numa conta nova falha com `BucketAlreadyExists` (409) ao tentar recriar o bucket com o mesmo nome. Não há como "retomar" um nome de bucket de uma conta Academy antiga.

**Contorno usado:** renomeado para `oficina-execucao-service-terraform-state-v2` em `infra/bootstrap/main.tf` e nos dois `backend.tf` (`infra/terraform/envs/homolog` e `envs/prod`). A tabela DynamoDB de lock manteve o nome (`oficina-execucao-service-lock`) — nomes de tabela DynamoDB são únicos só por conta/região, não globalmente, então não colide.

**Implicação para o resto do grupo:** qualquer bucket S3 criado por Terraform num repositório (não só aqui) pode colidir desse jeito se o nome já foi usado numa sessão Academy anterior. Se acontecer: renomear (sufixo `-v2`, `-v3`, etc.) em todos os arquivos que referenciam o nome — normalmente o recurso que cria o bucket e qualquer `backend "s3"` que aponte pra ele, já que blocos de backend do Terraform não aceitam variáveis, só literais.
