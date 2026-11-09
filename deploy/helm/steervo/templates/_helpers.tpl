{{- define "steervo.name" -}}
{{- printf "%s-steervo" .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- define "steervo.image" -}}
{{- if .Values.image.digest -}}
{{ .Values.image.repository }}@{{ .Values.image.digest }}
{{- else -}}
{{ .Values.image.repository }}:{{ .Values.image.tag }}
{{- end -}}
{{- end -}}
{{- define "steervo.env" -}}
- name: ASPNETCORE_ENVIRONMENT
  value: Production
- name: ASPNETCORE_HTTP_PORTS
  value: "8080"
- name: PublicUrl
  value: {{ .Values.publicUrl | quote }}
- name: AllowedHosts
  value: {{ .Values.ingress.host | quote }}
- name: DataProtection__Path
  value: /var/steervo/keys
- name: Smtp__Host
  value: {{ .Values.smtp.host | quote }}
- name: Smtp__Port
  value: {{ .Values.smtp.port | quote }}
- name: Smtp__StartTls
  value: {{ .Values.smtp.startTls | quote }}
- name: Smtp__From
  value: {{ .Values.smtp.from | quote }}
- name: Smtp__HourlyLimit
  value: {{ .Values.smtp.hourlyLimit | quote }}
- name: Smtp__DailyLimit
  value: {{ .Values.smtp.dailyLimit | quote }}
{{- range $index, $proxy := .Values.trustedProxies }}
- name: TrustedProxies__{{ $index }}
  value: {{ $proxy | quote }}
{{- end }}
{{- end -}}
