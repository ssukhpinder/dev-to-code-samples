variable "project_name" {
  description = "Display name for the OpenAI project managed by Terraform."
  type        = string
  default     = "terraform-managed-demo"

  validation {
    condition     = length(trimspace(var.project_name)) > 0
    error_message = "project_name must contain at least one non-whitespace character."
  }
}
