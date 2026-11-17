# Pacote portátil do app (Windows 11 x64)

**Classe: INTERNET** para a sinalização; **LOCAL** para a mídia (app e bot na mesma máquina). Endereço do servidor omitido de propósito (repositório público).

## O que é
`deploy\publish-app.ps1 -Server wss://<host>/ws` gera `artifacts\app\Coconut-win-x64.zip` (~69 MB, 477 arquivos):
- `dotnet publish` autocontido (.NET 10 + Windows App SDK 2.6.5); nada a instalar.
- `coconut.defaults.json` ao lado do exe com o servidor; vale só enquanto a pessoa não tem `%LOCALAPPDATA%\Coconut\settings.json` (5 testes em `JsonSettingsStoreTests`).
- Runtime do VC++ local (`msvcp140`, `vcruntime140`, `vcruntime140_1`, Build Tools 14.44): as DLLs nativas os importam (conferido com `dumpbin /dependents`); o UCRT já vem no Windows.
- LEIA-ME em português, `LICENCA.txt` (GPLv3), link do código-fonte e `licencas-terceiros\` (copyrights do vcpkg + lista de componentes .NET/WinAppSDK/VC++).
- Sem assinatura digital: o SmartScreen avisa (instruções no LEIA-ME). Instalador e atualização verificada continuam pendentes (P7).

## Problemas encontrados e corrigidos
1. **`dotnet publish` omitia `Coconut.pri` e os `.xbf`** (WinUI não empacotado): a UI não carregaria. Corrigido com `EnableMsixTooling=true` (não gera MSIX); o script falha se `Coconut.pri` faltar.
2. **Servidor: laço infinito na segunda sala do mesmo endereço.** Ao fechar uma sala, a contagem por origem voltava a 0 mas a chave ficava; a criação seguinte chamava `TryAdd` (que falhava) para sempre, girando uma thread (5 threads a ~50% de CPU na VM) e deixando o app em "Conectando…" até o prazo de 30 s. Só aparecia depois da primeira sala fechada — o teste de fumaça local sobe um servidor novo a cada vez. Corrigido em `RoomRegistry` (decisão por presença da chave; endereço sem salas sai da memória), com regressão `Room_slot_is_released_when_the_room_closes` (falhava antes, passa depois) e reimplantado na VM.

## Verificado (2026-11-16)
| Verificação | Resultado |
|---|---|
| Zip extraído numa pasta nova, configurações do usuário removidas (`ui-smoke.ps1 -AppPath … -FreshSettings`) | app usou o servidor do `coconut.defaults.json` e o gravou nas configurações |
| Criar sala → bot entra → remover → sair, contra a VM, **duas vezes seguidas** | ok nas duas; enlace P2P verificado em 114 ms e 67 ms |
| Origem das DLLs no processo | `datachannel`, `msvcp140`, `vcruntime140`, `vcruntime140_1` carregadas da pasta do pacote |
| Capturas da UI do pacote | tela inicial com aviso de privacidade; chamada com 2 pessoas, convite, mudo, sair |
| `dotnet test Coconut.slnx` | 508/508 |

## Não verificado
- PC **sem** o VC++ Redistributable instalado (este tem 14.51 em System32; o carregamento local foi observado, mas não num Windows limpo).
- SmartScreen e firewall no PC de outra pessoa; chamada real entre duas pessoas em redes diferentes.
