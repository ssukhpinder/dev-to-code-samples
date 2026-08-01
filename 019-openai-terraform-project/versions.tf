terraform {
  required_version = ">= 1.7.0"

  required_providers {
    openai = {
      source  = "openai/openai"
      version = "1.0.0"
    }
  }
}
