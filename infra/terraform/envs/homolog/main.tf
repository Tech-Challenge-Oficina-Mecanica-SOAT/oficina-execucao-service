terraform {
  required_version = ">= 1.9.0"
  required_providers {
    aws = { source = "hashicorp/aws", version = ">= 5.70, < 7.0" }
  }
}

provider "aws" {
  region = "us-east-1"
}

data "aws_iam_role" "lab_role" {
  name = "LabRole"
}

module "execucao_table" {
  source      = "../../modules/execucao-table"
  environment = var.environment
}

module "execucao_messaging" {
  source      = "../../modules/execucao-messaging"
  environment = var.environment
}
