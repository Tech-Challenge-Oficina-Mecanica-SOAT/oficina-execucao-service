terraform {
  required_version = ">= 1.9.0"
  required_providers {
    aws = { source = "hashicorp/aws", version = ">= 5.70, < 7.0" }
  }
}

provider "aws" {
  region = "us-east-1"
}

resource "aws_s3_bucket" "terraform_state" {
  # "-v2": o nome original ficou indisponível globalmente (bucket S3 reservado por uma sessão
  # anterior/efêmera da AWS Academy que não foi liberado). Ver docs/ARCHITECTURE.md.
  bucket = "oficina-execucao-service-terraform-state-v2"

  tags = {
    Name        = "oficina-execucao-service-terraform-state-v2"
    Environment = "infra"
    ManagedBy   = "terraform"
  }
}

resource "aws_s3_bucket_versioning" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "terraform_state" {
  bucket                  = aws_s3_bucket.terraform_state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# Bucket dedicado para os logs de acesso do bucket de state (achado SonarCloud terraform:S6258).
# Não loga para si mesmo: evitaria um ciclo de auto-referência (cada log gravado geraria outro log).
resource "aws_s3_bucket" "terraform_state_logs" {
  bucket = "oficina-execucao-service-terraform-state-logs"

  tags = {
    Name        = "oficina-execucao-service-terraform-state-logs"
    Environment = "infra"
    ManagedBy   = "terraform"
  }
}

resource "aws_s3_bucket_public_access_block" "terraform_state_logs" {
  bucket                  = aws_s3_bucket.terraform_state_logs.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

data "aws_caller_identity" "current" {}

# Buckets com Object Ownership "bucket owner enforced" (padrão desde 2023) desabilitam ACLs,
# então a entrega de log exige política de bucket para o serviço logging.s3.amazonaws.com,
# não o ACL canned "log-delivery-write" usado em exemplos mais antigos.
resource "aws_s3_bucket_policy" "terraform_state_logs" {
  bucket = aws_s3_bucket.terraform_state_logs.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "S3ServerAccessLogsPolicy"
        Effect    = "Allow"
        Principal = { Service = "logging.s3.amazonaws.com" }
        Action    = "s3:PutObject"
        Resource  = "${aws_s3_bucket.terraform_state_logs.arn}/*"
        Condition = {
          ArnLike      = { "aws:SourceArn" = aws_s3_bucket.terraform_state.arn }
          StringEquals = { "aws:SourceAccount" = data.aws_caller_identity.current.account_id }
        }
      }
    ]
  })
}

resource "aws_s3_bucket_logging" "terraform_state" {
  bucket        = aws_s3_bucket.terraform_state.id
  target_bucket = aws_s3_bucket.terraform_state_logs.id
  target_prefix = "terraform-state-access-logs/"
}

# Nega qualquer acesso que não seja via HTTPS (achado SonarCloud terraform:S6249).
resource "aws_s3_bucket_policy" "terraform_state_https_only" {
  bucket = aws_s3_bucket.terraform_state.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "DenyInsecureTransport"
        Effect    = "Deny"
        Principal = "*"
        Action    = "s3:*"
        Resource = [
          aws_s3_bucket.terraform_state.arn,
          "${aws_s3_bucket.terraform_state.arn}/*"
        ]
        Condition = {
          Bool = { "aws:SecureTransport" = "false" }
        }
      }
    ]
  })
}

resource "aws_dynamodb_table" "terraform_lock" {
  name         = "oficina-execucao-service-lock"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "LockID"

  attribute {
    name = "LockID"
    type = "S"
  }

  tags = {
    Name        = "oficina-execucao-service-lock"
    Environment = "infra"
    ManagedBy   = "terraform"
  }
}
