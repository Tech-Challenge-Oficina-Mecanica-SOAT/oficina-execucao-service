output "queue_url" {
  value = aws_sqs_queue.execucao_queue.url
}

output "queue_arn" {
  value = aws_sqs_queue.execucao_queue.arn
}

output "dlq_arn" {
  value = aws_sqs_queue.execucao_dlq.arn
}

output "published_topic_arns" {
  value = { for k, v in aws_sns_topic.published : k => v.arn }
}
