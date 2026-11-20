// Values only, never secrets. azd sets AZURE_ENV_NAME and AZURE_LOCATION; the defaults let
// `az bicep build-params` check this file offline.
using 'main.bicep'

param environmentName = readEnvironmentVariable('AZURE_ENV_NAME', 'demo')
param location = readEnvironmentVariable('AZURE_LOCATION', 'centralus')
param operatorPrincipalId = readEnvironmentVariable('STRIA_OPERATOR_PRINCIPAL_ID', '')
