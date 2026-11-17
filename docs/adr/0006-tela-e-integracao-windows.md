# ADR 0006 — tela e integração Windows do MVP

Data: 2026-11-17. Estado: aceita, implementada; validações externas no estado.

## Tela

`Coconut.Screen` encapsula `Coconut.Screen.Native.dll`, ponte C++20/WinRT:
Windows Graphics Capture de monitor inteiro → D3D11 BGRA/NV12 → Media Foundation
H.264. Prefere MFT de hardware assíncrona, com alternativa por software. Texturas
de amostras têm vida própria até o encoder liberá-las. Não há arquivo de vídeo.
O receptor usa Media Foundation e swapchain D3D11 ligada ao `SwapChainPanel` WinUI.

Codifica uma vez e replica os pacotes para pares admitidos/verificados.
RTP H.264 (RFC 6184, single NAL e FU-A) carrega extensão RFC 8285 com os
128 bits de `ShareSessionId` e dimensões em cada pacote. O receptor exige
peer autenticado e sessão anunciada pelo servidor antes de acumular dados.
Limites de tamanho, fragmentos/NALs e dimensões contêm alocações.
Perda exige novo IDR com SPS/PPS; os parâmetros são repetidos nos IDRs para
entrada tardia. Mudança de transmissor, sessão ou sinalização limpa a recepção.
O frame conserva identidade/sessão original até a última verificação na UI.

O servidor continua sendo a única autoridade. Aceitar troca não concede
captura local: o anterior para e confirma `ShareStopped`, e só
`ShareStateChanged` autoriza o próximo. Solicitar, recusar, cancelar e expirar
seguem o protocolo existente. Falha de captura para vídeo e informa a pessoa.

RTCP SR/RR mede perda e RTT por receptor. O pior receptor atual reduz bitrate,
FPS e resolução; recuperação gradual limitada à preferência. Relatórios
expiram, pares que saem são removidos. Congestionamento severo persistente para
vídeo; áudio tem trilha independente. Não garante voz perfeita em toda rede.

## Desktop e retomada

WinUI não empacotado usa Shell_NotifyIcon e subclassificação segura da janela.
Fechar oculta; encerrar libera chamada, captura e nativos. O renderizador é
descarregado quando oculto; a chamada permanece ativa.
Mutex de presença e named pipe `CurrentUserOnly` encaminham ativações limitadas
em tamanho. Links são estritos, sem execução e sem autoentrada.
HKCU registra protocolo e inicialização opcional `--startup` (desligada por
padrão). O instalador preserva essa escolha e remove apenas registros próprios.

Sinalização perdida suspende autorização de vídeo e tenta retomar por até
25 segundos com token em memória, dentro da tolerância de 30 segundos do servidor.
Snapshot autenticado restaura o estado. Áudio P2P pode continuar. Enlaces rompidos
são renegociados pelo ofertante, com tentativas limitadas; a UI diferencia
sinalização, admissão, P2P e áudio efetivamente decodificado.

## Distribuição e atualização

Mantemos Inno Setup e o payload autocontido adotados na 0.2.0 em vez de
introduzir Velopack (hipótese inicial de P7). Um cliente pequeno verifica
manifesto Ed25519 e hash/tamanho antes de iniciar o mesmo instalador. Evita dois
sistemas de instalação e preserva AppId/diretório/preferências existentes.
NSec/libsodium têm licenças compatíveis e avisos incluídos no pacote.

Chave pública embutida no app. O gerador mantém a chave privada sob DPAPI
CurrentUser fora do repositório, em `%LOCALAPPDATA%\Coconut\signing`.
Essa chave não entra em binários, logs exportados ou assets públicos. Sua perda
exige migração explícita de confiança; não regenerar silenciosamente.

Manifesto limita runtime, versão crescente, validade, tamanho, hash e URL ao
instalador canônico da release neste repositório. Downloads HTTPS são limitados
e redirects restritos aos hosts de assets do GitHub. Hash é conferido novamente
sob bloqueio de escrita imediatamente antes de CreateProcess. Versão máxima
conhecida evita rollback já observado. Não aplica com chamada/entrada ativa.
Verificação/download automáticos; instalação exige clique quando ocioso.

Ed25519 não substitui certificado de editor Authenticode. Não há certificado
configurado; avisos SmartScreen continuam possíveis. Windows limpo e redes
externas precisam de validação; MVP publicado como pré-lançamento.
