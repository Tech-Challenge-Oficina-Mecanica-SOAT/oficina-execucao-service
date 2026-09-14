resource "aws_sqs_queue" "execucao_dlq" {
  name = "oficina-${var.environment}-execucao-dlq"

  tags = {
    Project     = "oficina-execucao-service"
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

resource "aws_sqs_queue" "execucao_queue" {
  name = "oficina-${var.environment}-execucao-queue"

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.execucao_dlq.arn
    maxReceiveCount     = 3
  })

  tags = {
    Project     = "oficina-execucao-service"
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

resource "aws_ssm_parameter" "queue_url" {
  name  = "/oficina/${var.environment}/execucao/queue-url"
  type  = "String"
  value = aws_sqs_queue.execucao_queue.url
}

locals {
  published_topics = ["diagnostico.concluido", "execucao.iniciada", "execucao.finalizada"]
}

resource "aws_sns_topic" "published" {
  for_each = toset(local.published_topics)
  name     = "oficina-${var.environment}-${replace(each.value, ".", "-")}"

  tags = {
    Project     = "oficina-execucao-service"
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

resource "aws_ssm_parameter" "published_topic_arns" {
  for_each = aws_sns_topic.published
  name     = "/oficina/${var.environment}/messaging/topics/${each.key}/arn"
  type     = "String"
  value    = each.value.arn
}

# ─── Assinaturas (SQS ⟵ SNS de outros serviços) ──────────────────
# Bloqueado: os parâmetros abaixo só existem depois que OS Service e
# Billing Service aplicarem os próprios topics SNS (Semana 3 deles).
# Na data em que este módulo foi escrito, nenhum dos dois chegou lá.
# O código fica pronto; só falta rodar `terraform apply` depois que
# esses parâmetros existirem.

locals {
  consumed_topics = ["os.criada", "os.cancelada", "orcamento.aprovado"]
}

data "aws_ssm_parameter" "consumed_topic_arns" {
  for_each = toset(local.consumed_topics)
  name     = "/oficina/${var.environment}/messaging/topics/${each.value}/arn"
}

resource "aws_sns_topic_subscription" "consumed" {
  for_each             = toset(local.consumed_topics)
  topic_arn            = data.aws_ssm_parameter.consumed_topic_arns[each.value].value
  protocol             = "sqs"
  endpoint             = aws_sqs_queue.execucao_queue.arn
  raw_message_delivery = true
}

resource "aws_sqs_queue_policy" "allow_sns" {
  queue_url = aws_sqs_queue.execucao_queue.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      for topic in local.consumed_topics : {
        Effect    = "Allow"
        Principal = { Service = "sns.amazonaws.com" }
        Action    = "sqs:SendMessage"
        Resource  = aws_sqs_queue.execucao_queue.arn
        Condition = {
          ArnEquals = { "aws:SourceArn" = data.aws_ssm_parameter.consumed_topic_arns[topic].value }
        }
      }
    ]
  })
}
