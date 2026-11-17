# Primeira chamada real: sem áudio nos dois sentidos (investigação)

**Classe: INTERNET** (relato do usuário) e **LOCAL** (reprodução). Causa da chamada real: **não identificada**.

## Relato (2026-11-16)
Usuário e amigo, em redes diferentes, entraram na mesma sala com o pacote 0.1.0 (ambos admitidos, logo com enlace P2P verificado pelo canal de dados) e não se ouviram. Sintomas informados: a barra do microfone mexia; ninguém viu a borda de fala no cartão do outro; trocaram de dispositivo durante a chamada; nenhuma mensagem de erro. Usuário com headset Logitech G435.

## O que foi testado neste PC (todos funcionaram)
Bot com áudio (`Tools.CallBot … --audio`: envia tom de 440 Hz e mede o que recebe):
| Cenário | Envio do app (medido no bot) | Recepção do app (borda de fala) |
|---|---|---|
| App do pacote cria sala pela nuvem + bot | 691 pacotes, todos decodificados | sim |
| Dois apps (criador e convidado) + bot pela nuvem | 694 e 693 pacotes | sim nos dois |
| Troca de saída e microfone para o G435 durante a chamada | contínuo, ~50 pacotes/s antes e depois | sim, depois da troca |

## Conclusão e o que mudou (0.1.1)
O caminho de áudio funciona nos dois papéis, com troca de dispositivo e pela nuvem. O que resta é o ambiente real (PC do amigo, fone Bluetooth, privacidade do microfone, driver) ou a rede entre os dois — invisível porque o app **descartava erros de áudio em silêncio** e não registrava nada:
- `CallBackend` iniciava a voz num `Task.Run` sem tratamento; `VoiceEngine.Reopen` só tratava `AudioDeviceUnavailableException`, e a saída abria antes do microfone (falha na saída impedia o microfone). Agora toda falha vira aviso na tela (com código HRESULT) e registro; cada direção abre independente.
- **Registro de diagnóstico local** (SEC-2): `%LOCALAPPDATA%\Coconut\logs\coconut-AAAAMMDD.log`, 7 dias, teto de 5 MB/dia, sanitizado (IPs v4/v6/sslip, convites, tokens, SDP/candidatos; 15 testes). A cada 5 s: quadros capturados/codificados/entregues, quadros tocados pela saída, pacotes recebidos por par (atrasados, descartados, ocultados); tipo de conexão por par (host/srflx/prflx); dispositivos abertos com nome e efeitos.

## Achados laterais
- Ao **reabrir** o microfone (troca de dispositivo), a supressão de ruído por IA do Windows aparece ligada; na primeira abertura fica desligada. Contraria o "áudio natural" (ADR 0004); não silencia a voz. Pendente.
- A solução compilava o app em `bin\x64\Debug` e o exe documentado (`bin\Debug`) ficava antigo; corrigido (`AppendPlatformToOutputPath=false`).
- O tom do bot tocou no alto-falante do monitor durante os testes (amplitude reduzida depois).

## Próximo passo
Nova chamada real com 0.1.1 e os dois arquivos de log.
