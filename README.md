# Coconut

Aplicativo open source de voz e compartilhamento de tela para Windows 11 x64, com até 15 participantes e mídia ponto a ponto via WebRTC. MVP em pré-lançamento — veja [estado e validações](docs/status.md).

<img src="src/Coconut.App/Assets/Coconut.png" width="96" height="96" alt="Ícone Coconut: coco com ondas de voz">

## Baixar e instalar

**[Baixar o instalador do Coconut 0.3.0 para Windows 11 x64](https://github.com/Tarcitani/coconut-call/releases/download/v0.3.0/Coconut-0.3.0-win-x64-Setup.exe)** · [Lançamentos e versão portátil](https://github.com/Tarcitani/coconut-call/releases)

1. Execute o arquivo `Setup.exe` e siga o assistente. Todas as dependências estão incluídas; a instalação funciona offline e não exige administrador.
2. Abra **Coconut** pelo menu Iniciar.
3. Peça o endereço `wss://…/ws` a quem hospeda sua chamada e informe em **Avançado → Servidor de sinalização**. O pacote público não inclui um servidor. Todos precisam usar o mesmo endereço.
4. Digite seu nome e crie uma sala ou cole o convite para entrar.

O instalador ainda não tem assinatura de editor Authenticode; o Windows pode mostrar um aviso do SmartScreen. As atualizações a partir da 0.3.0 verificam um manifesto Ed25519 e o hash do instalador antes de executá-lo. Isso não substitui Authenticode. O `SHA256SUMS.txt` também permite conferir arquivos com `Get-FileHash`. Para atualizar manualmente, feche o app e execute o instalador novo. As preferências são preservadas; a remoção fica em **Configurações → Aplicativos → Aplicativos instalados**.

## Usar o MVP

- Voz Opus com mudo, seleção de dispositivos e indicadores separados de conexão e áudio decodificado.
- **Compartilhar tela**: escolha um monitor inteiro, resolução, FPS e bitrate por participante. H.264 usa hardware quando disponível, com alternativa por software. Há um transmissor por sala; outra pessoa pode pedir a troca, que precisa ser aceita pelo transmissor atual.
- Fechar a janela mantém a chamada na bandeja. O menu da bandeja permite abrir, silenciar e encerrar. A inicialização com o Windows é opcional e vem desligada.
- Links `coconut://join/…` abrem a instância existente e preenchem o convite; entrar exige ação da pessoa. Um convite recebido durante uma chamada não troca a sala.
- Na engrenagem: inicialização, atualização verificada, exportação de diagnóstico sanitizado e encerramento.

O vídeo adapta a qualidade ou para em congestionamento persistente para liberar a conexão para a voz. Em malha, o upload do transmissor cresce com o número de receptores. Não há captura de som do sistema nem retransmissão TURN: certas redes impedem a conexão direta. Windows limpo, redes distintas, acessibilidade completa e estabilidade prolongada ainda precisam de validação externa.

## Arquitetura e compilação

O cliente usa C#/.NET 10, WinUI 3 e WASAPI, com Opus e WebRTC nativos. A tela usa Windows Graphics Capture, Media Foundation e D3D11. O servidor ASP.NET Core coordena salas e a sinalização; áudio e vídeo seguem diretamente entre os participantes. [Arquitetura e distribuição para Windows](docs/distribuicao-windows.md) descreve módulos, dependências e comandos de build.

**Privacidade:** a mídia trafega diretamente entre participantes, por isso cada participante vê o endereço IP dos demais.

Licença: [GPLv3](LICENSE).
