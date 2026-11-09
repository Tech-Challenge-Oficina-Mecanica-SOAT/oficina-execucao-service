terraform {
  backend "s3" {
    bucket         = "oficina-execucao-service-terraform-state-v2"
    key            = "prod/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "oficina-execucao-service-lock"
    encrypt        = true
  }
}
