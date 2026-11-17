# Instalador Windows — Coconut 0.2.0

Data: 2026-11-16 (America/Sao_Paulo). **Classe: LOCAL**. Não representa teste em Windows limpo nem chamada entre duas redes.

## Artefatos finais

Gerados com `powershell -NoProfile -ExecutionPolicy Bypass -File deploy\publish-installer.ps1`, sem `-Server`, usando Inno Setup 7.1.1. Versão do produto e arquivo: `0.2.0` / `0.2.0.0`. O SHA da revisão anterior não é acrescentado à versão informativa dos pacotes; `CODIGO-FONTE.txt` aponta para a tag da versão.

| Arquivo | Bytes |
|---|---:|
| `Coconut-0.2.0-win-x64-Setup.exe` | 49.901.229 |
| `Coconut-0.2.0-win-x64.zip` | 72.735.534 |

Hashes publicados em `Coconut-0.2.0-win-x64-SHA256SUMS.txt`. O payload tem 479 arquivos. Não contém `coconut.defaults.json`; o servidor particular não foi incluído.

## Verificações observadas

| Verificação | Resultado |
|---|---|
| `dotnet test Coconut.slnx -c Release` | 523 aprovados: 362 núcleo, 84 servidor, 23 áudio, 54 App.Core |
| Publish do app e compilação do instalador | Sucesso; nenhum aviso/erro encontrado no log de build |
| ICO original | Nove tamanhos: 16, 20, 24, 32, 40, 48, 64, 128 e 256; PNG inspecionado |
| Instalação de uma baseline local com o payload portátil 0.1.1 e atualização pelo Setup 0.2.0 | Sucesso; versão passou de 0.1.1 para 0.2.0; diretório original reutilizado sem fornecer `/DIR` na atualização |
| Privilégios | Log: `User privileges: None`, `Administrative install mode: No`, chave raiz `HKEY_CURRENT_USER` |
| Arquivos instalados | 479/479 iguais por SHA-256 ao payload |
| Atalho no menu Iniciar | Aponta para o Coconut.exe instalado |
| Abertura do app instalado | Processo iniciado; controles da tela inicial observados pela árvore de acessibilidade |
| Runtimes carregados | `hostfxr.dll`, `coreclr.dll` e `Microsoft.UI.Xaml.dll` da pasta de instalação, conferidos nos módulos do processo |
| Reinstalação do pacote final | Sucesso; versão informativa exatamente 0.2.0 |
| Atalho opcional na área de trabalho | Criado com `/TASKS=desktopicon`; destino conferido |
| Zip final extraído numa pasta nova | 479/479 arquivos iguais por SHA-256 ao payload e à instalação final |
| SHA256SUMS | Recalculado e comparado com os dois arquivos finais |
| Desinstalação silenciosa | Código 0; executável, registro de desinstalação e ambos os atalhos removidos; log `Removed all? Yes` |
| Preferências do usuário | Hash de `settings.json` preservado na atualização, reinstalação e desinstalação |

Baseline de instalação, extração e logs ficam em `artifacts/installer-validation/` (ignorados pelo Git). A baseline 0.1.1 foi produzida somente para este teste; não é um artefato público. O teste foi instalado em subdiretório de `artifacts`, sem substituir instalação anterior do usuário. A instalação de teste foi removida ao terminar.

## Limites

- A captura de imagem do helper não correspondeu à janela do aplicativo, embora a árvore de acessibilidade correspondesse. O layout e o ícone da janela em execução **não** são considerados visualmente validados neste incremento. O desenho do PNG foi inspecionado, os recursos ICO estão no executável/payload e `AppWindow.SetIcon` foi executado sem impedir a abertura.
- Não foi repetido o teste de sala/áudio neste incremento; as verificações de execução cobriram a inicialização. Os testes unitários/integração existentes passaram.
- Este computador já tem runtimes instalados. Carregamento local e presença das dependências foram verificados, mas um Windows limpo ainda precisa ser testado.
- Fluxo interativo completo do assistente, SmartScreen em outro PC, Windows ARM64 e assinatura Authenticode não foram validados. ARM64 é recusado pelo instalador.
- Assinatura digital e atualização automática verificada continuam pendentes; atualização manual pelo novo instalador está implementada e foi exercitada.
