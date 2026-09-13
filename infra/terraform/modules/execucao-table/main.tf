resource "aws_dynamodb_table" "execucao" {
  name         = "oficina-execucao-${var.environment}"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "PK"
  range_key    = "SK"

  attribute {
    name = "PK"
    type = "S"
  }

  attribute {
    name = "SK"
    type = "S"
  }

  attribute {
    name = "status"
    type = "S"
  }

  attribute {
    name = "adicionadoEm"
    type = "S"
  }

  global_secondary_index {
    name            = "status-index"
    hash_key        = "status"
    range_key       = "adicionadoEm"
    projection_type = "ALL"
  }

  tags = {
    Project     = "oficina-execucao-service"
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

resource "aws_ssm_parameter" "table_name" {
  name  = "/oficina/${var.environment}/execucao/table-name"
  type  = "String"
  value = aws_dynamodb_table.execucao.name
}
