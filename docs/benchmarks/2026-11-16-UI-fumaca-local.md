# App WinUI 3 — teste de fumaça de ponta a ponta (local)

**Classe: LOCAL**, uma máquina. Comprova que o app real cria a sala, admite outro participante por P2P, mostra a lista, expulsa pela UI e volta ao início. **Não** comprova chamada entre duas máquinas, qualidade de voz nem tema claro.

## Procedimento
```
dotnet build Coconut.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File prototypes\Tools.UiSmoke\ui-smoke.ps1
```
Servidor local (127.0.0.1:5080) → app `Coconut.exe` (WinUI 3, Windows App SDK 2.6.5 autocontido, não empacotado) → UI Automation preenche "Seu nome" e aciona "Criar sala" → o convite é lido da tela → `Tools.CallBot` (participante sem áudio) entra → "Remover da sala" → "Sair". Capturas via `PrintWindow` em `prototypes/Tools.UiSmoke/runs/` (não versionadas).

## Resultado (2026-11-16)
- Tela inicial: aviso de privacidade sobre IP, botões desabilitados sem nome, tema escuro do sistema.
- Sala criada: "1 pessoa", convite `XXXXX-XXXXX` legível e copiável, "Você · administrador", "Encerrar para todos" visível.
- Bot: enlace P2P com o app verificado em 40–49 ms, admitido com 2 participantes; cartão com iniciais e botão de remover (só nos outros, nunca em si mesmo).
- Remover pela UI: bot recebeu `kicked`; lista voltou a "1 pessoa".
- Sair: volta ao início sem erro; nome lembrado.

## Defeitos encontrados pela inspeção visual (corrigidos)
1. Cartão de participante sem nome/estado e com botão de remover no próprio usuário: o `ParticipantItem` dependia do `DataContext` dentro do `ItemsRepeater` e as ligações não resolviam. Agora recebe o participante por propriedade explícita (`Participant="{x:Bind}"`), e o botão fica oculto por padrão.
2. (Testes de ViewModel) A pré-seleção do dispositivo padrão fixava o dispositivo e abandonava "seguir o padrão do Windows".

## Não verificado
Indicador de fala acendendo (o bot não envia áudio), painel de dispositivos aberto, mensagens de erro em tela, tema claro, leitor de tela, redimensionamento extremo, alto contraste.
