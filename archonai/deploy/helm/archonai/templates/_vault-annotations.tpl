{{/*
Vault Agent Injector annotations for HashiCorp Vault sidecar secret injection.
These annotations are added to pod templates when secrets.provider=vault-injector.
Secrets are mounted as files at /vault/secrets/ and read by FileSecretProvider.
*/}}
{{- define "archonai.vaultAnnotations" -}}
{{- if eq .Values.secrets.provider "vault-injector" }}
vault.hashicorp.com/agent-inject: "true"
vault.hashicorp.com/role: {{ .Values.secrets.vault.role | quote }}
vault.hashicorp.com/agent-inject-secret-jwt-signing-key: {{ .Values.secrets.vault.secretPath | quote }}
vault.hashicorp.com/agent-inject-template-jwt-signing-key: |
  {{`{{- with secret "`}}{{ .Values.secrets.vault.secretPath }}{{`" }}{{ .Data.data.jwt_signing_key }}{{ end }}`}}
{{- if .Values.secrets.vault.dbSecretPath }}
vault.hashicorp.com/agent-inject-secret-db-password: {{ .Values.secrets.vault.dbSecretPath | quote }}
vault.hashicorp.com/agent-inject-template-db-password: |
  {{`{{- with secret "`}}{{ .Values.secrets.vault.dbSecretPath }}{{`" }}{{ .Data.data.password }}{{ end }}`}}
vault.hashicorp.com/agent-inject-secret-db-username: {{ .Values.secrets.vault.dbSecretPath | quote }}
vault.hashicorp.com/agent-inject-template-db-username: |
  {{`{{- with secret "`}}{{ .Values.secrets.vault.dbSecretPath }}{{`" }}{{ .Data.data.username }}{{ end }}`}}
{{- end }}
vault.hashicorp.com/agent-pre-populate-only: "false"
vault.hashicorp.com/agent-cache-enable: "true"
{{- end }}
{{- end -}}

{{/*
Environment variables for Vault file-based secret provider.
Sets ARCHONAI_VAULT_SECRETS_PATH so the application uses FileSecretProvider.
*/}}
{{- define "archonai.vaultEnv" -}}
{{- if eq .Values.secrets.provider "vault-injector" }}
- name: ARCHONAI_VAULT_SECRETS_PATH
  value: "/vault/secrets"
{{- end }}
{{- end -}}

{{/*
Volume mount for Vault secrets (tmpfs for readOnlyRootFilesystem compat).
*/}}
{{- define "archonai.vaultVolumeMount" -}}
{{- if eq .Values.secrets.provider "vault-injector" }}
- name: vault-secrets
  mountPath: /vault/secrets
  readOnly: true
{{- end }}
{{- end -}}

{{/*
Volume definition for Vault secrets.
*/}}
{{- define "archonai.vaultVolume" -}}
{{- if eq .Values.secrets.provider "vault-injector" }}
- name: vault-secrets
  emptyDir:
    medium: Memory
    sizeLimit: 1Mi
{{- end }}
{{- end -}}
