mock_provider "openai" {}

run "project_name_reaches_resource" {
  command = plan

  variables {
    project_name = "terraform-managed-test"
  }

  assert {
    condition     = openai_project.managed.name == "terraform-managed-test"
    error_message = "The configured project name did not reach openai_project.managed."
  }
}
