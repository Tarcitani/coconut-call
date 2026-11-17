# Coconut

Aplicativo open source de chamadas de voz (individuais e em grupo, até 15 pessoas) para Windows 11 x64, com mídia ponto a ponto via WebRTC. Em pré-lançamento; compartilhamento de tela está planejado — veja [docs/status.md](docs/status.md).

<img src="src/Coconut.App/Assets/Coconut.png" width="96" height="96" alt="Ícone Coconut: coco com ondas de voz">

## Baixar e instalar

**[Baixar o instalador do Coconut 0.2.0 para Windows 11 x64](https://github.com/Tarcitani/coconut-call/releases/download/v0.2.0/Coconut-0.2.0-win-x64-Setup.exe)** · [Lançamentos e versão portátil](https://github.com/Tarcitani/coconut-call/releases)

1. Execute o arquivo `Setup.exe` e siga o assistente. Todas as dependências estão incluídas; a instalação funciona offline e não exige administrador.
2. Abra **Coconut** pelo menu Iniciar.
3. Peça o endereço `wss://…/ws` a quem hospeda sua chamada e informe em **Avançado → Servidor de sinalização**. O pacote público não inclui um servidor. Todos precisam usar o mesmo endereço.
4. Digite seu nome e crie uma sala ou cole o convite para entrar.

O instalador e o app ainda não têm assinatura digital; o Windows pode mostrar um aviso do SmartScreen. O arquivo `SHA256SUMS.txt` da release permite conferir a integridade com `Get-FileHash` no PowerShell. Para atualizar, feche o app e execute o instalador novo. Para remover, use **Configurações → Aplicativos → Aplicativos instalados**. As preferências em `%LOCALAPPDATA%\Coconut` são preservadas.

## Arquitetura e compilação

O cliente usa C#/.NET 10, WinUI 3 e WASAPI, com Opus e WebRTC nativos. O servidor ASP.NET Core coordena salas e a sinalização; o áudio segue diretamente entre os participantes. [Arquitetura e distribuição para Windows](docs/distribuicao-windows.md) descreve os módulos, a solução de instalação, dependências e comandos de build.

**Privacidade:** a mídia trafega diretamente entre participantes, por isso cada participante vê o endereço IP dos demais.

Licença: [GPLv3](LICENSE).
