# Coconut — app de voz e compartilhamento de tela P2P para Windows 11

Independente do mod de Minecraft homônimo (`C:\MinecraftDev\Coconut`): não reutilize nada de lá.

## Regras fundamentais
- C#/.NET 10, WinUI 3 + Windows App SDK 2.x, GPLv3. Dependências só com licença compatível com GPLv3 (SIPSorcery é incompatível — ADR 0002).
- Mídia **somente P2P**. Nunca habilitar TURN, SFU ou relay sem autorização explícita do usuário.
- Servidor de sinalização é a única autoridade de composição, papéis e transmissor; o núcleo (`Coconut.Signaling.Core`) é síncrono, sem E/S, e testado com relógio manual.
- Escopo do MVP: voz e tela. Qualquer funcionalidade nova, despesa, publicação ou operação destrutiva exige autorização.
- Logs nunca contêm tokens, códigos de convite completos, SDP, candidatos ICE ou IPs.
- Não declarar compilação, teste, conexão ou comportamento visual que não foi observado. Estados: implementado ≠ testado ≠ validado.

## Navegação
- Estado e próxima ação: `docs/status.md`
- Requisitos rastreáveis: `docs/requirements/checklist.md`
- Protocolo de salas/admissão/tela: `docs/protocol/salas-e-admissao.md`
- Decisões: `docs/adr/` · Pesquisa: `docs/research/` · Protótipos e benchmarks: `docs/prototypes/plano.md` · Ameaças: `docs/security/`

## Comandos
```
dotnet build Coconut.slnx
dotnet test Coconut.slnx
dotnet run --project src/Coconut.Signaling.Server --urls http://127.0.0.1:5080   # sinalização local (ws://127.0.0.1:5080/ws)
src\Coconut.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Coconut.exe       # app WinUI
powershell -NoProfile -ExecutionPolicy Bypass -File prototypes\Tools.UiSmoke\ui-smoke.ps1   # fumaça de UI (abre o app)
```
UI: views em `src/Coconut.App/Views` (uma área por arquivo, x:Bind a ViewModels de `Coconut.App.Core`); lógica e testes ficam no Core. Inspecione a UI em execução (capturas) — compilação não comprova layout.
Distribuição Windows (versão padrão em `VERSION`): `deploy\publish-installer.ps1 [-Version x.y.z] [-Server wss://host/ws]` gera instalador offline, zip e SHA-256; veja `docs/distribuicao-windows.md`. Release pública: sempre sem `-Server`; arquivos `-servidor` são particulares. Só o zip: `deploy\publish-app.ps1`. Publicar Setup, zip, SHA256SUMS e coconut-update.json; versão atual em VERSION. Ícone vetorial e gerador: `src/Coconut.App/Assets`, `deploy/windows/build-icon.ps1`. Atualização Ed25519: `-SignUpdate` gera manifesto assinado (chave privada DPAPI fora do repo); não regenerar a identidade existente. Authenticode permanece pendente. Veja ADR 0006. Ponte de tela C++/WinRT é compilada via CMake do Visual Studio integrado ao build; nativos WebRTC seguem vcpkg. Testar extraído com `ui-smoke.ps1 -Server … -AppPath …\Coconut.exe -FreshSettings`.
Diagnóstico de áudio: `Tools.CallBot <ws> <convite> nome 30 --audio` (envia tom, mede o recebido); log do app em `%LOCALAPPDATA%\Coconut\logs` (sanitizado).
Implantação (VM gratuita, Caddy + STUN): `deploy\publish.ps1 -Runtime linux-arm64` e [guia](docs/deploy/vm-gratuita.md); scripts Linux em LF (`.gitattributes`).
Repositório público: https://github.com/Tarcitani/coconut-call — publicar exige autorização do usuário.
Nativos (vcpkg em `C:\Dev\tools\vcpkg`, ~7 min na primeira vez):
```
C:\Dev\tools\vcpkg\vcpkg.exe install --x-manifest-root=native --x-install-root=native/vcpkg_installed --triplet x64-windows
dotnet run --project prototypes/P1.WebRtcLoopback
```
A API nativa segue o `rtc.h` da versão compilada (0.24.6), não a documentação do branch principal da libdatachannel.

## Ao retomar
Leia `docs/status.md`, `git status`/`git log` e só os documentos afetados. Atualize o estado e o checklist ao concluir um incremento.
