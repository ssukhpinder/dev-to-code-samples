provider "openai" {}

resource "openai_project" "managed" {
  name = var.project_name
}
