# P3 — áudio real no Windows: dispositivos, efeitos por categoria e motor de voz (local)

**Classe: LOCAL**, hardware real. Mede o caminho de dispositivo e o processamento aplicado pelo Windows. **Não** avalia qualidade audível, eco percebido nem latência boca-ouvido — dependem de pessoas falando e ouvindo (pendente).

## Ambiente
Windows 11 Pro build 26300 · entradas: VEDO BM800 (USB, padrão de comunicação), Logitech G435 (sem fio), QCY H3 Pro (Bluetooth) · saídas: VG249Q1R (HDMI/AMD, padrão), G435, QCY H3 Pro.

## Procedimento
`dotnet run --project prototypes/P3.AudioProbe` — lista dispositivos; abre a entrada padrão com e sem categoria Communications; captura 2 s (só contagem de quadros e RMS, nada é gravado); reproduz 2 s de **silêncio digital** na saída padrão; sobe o `VoiceEngine` completo com mudo ligado e sem pares.

## Resultados

Efeitos reportados pelo Windows (`IAudioEffectsManager`) na entrada VEDO BM800:

| Efeito | Communications (após desligar IA) | Categoria padrão |
|---|---|---|
| Cancelamento de eco | ligado | desligado |
| Supressão de ruído simples | ligado (não ajustável) | ligado (não ajustável) |
| Controle automático de ganho | ligado (não ajustável) | desligado |
| Supressão de ruído profunda (IA) | **ligado por padrão → desligado pelo Coconut** (confirmado relendo, com o fluxo em execução) | desligado |
| Beamforming | desligado | desligado |

- Referência do AEC controlável em Communications (`IAcousticEchoCancellationControl` disponível); indisponível na categoria padrão.
- `SetAudioEffectState` antes de `Start`: aceito e **ignorado** (estado continuou ligado); depois de `Start`: efetivo.
- Captura: 99–100 quadros de 20 ms em ~2 s; buffer de 40 ms. RMS de ambiente silencioso entre 0,5 e 3,5 (o microfone entrega dados; silêncio digital daria 0).
- Reprodução: 101 quadros consumidos em 2,04 s; nenhum efeito de saída reportado.
- Motor de voz completo (mudo): entrada e saída abertas; 100 capturados, **0 codificados, 0 entregas**; nenhum problema.
- `GetStreamLatency` informa 0 ms em modo compartilhado orientado a evento — não serve como medida de latência.

## Testes automatizados relacionados
`tests/Coconut.Audio.Tests` (23): jitter buffer (reordenação, perda, atraso, duplicata, volta de sequência, limite de latência), mixer, montagem de quadros, e pipeline com Opus real — mudo verificado no áudio decodificado do outro lado, timestamp contínuo em mudo, mistura de dois pares, remoção de par, ocultação de perda. Os três testes de mudo foram confirmados por **mutação** (porta de mudo desativada ⇒ falham).

## Pendente
- Chamada de voz real entre duas máquinas, com pessoas: inteligibilidade, eco com caixa de som, latência boca-ouvido.
- Desconexão física de dispositivo durante a chamada (código de recuperação implementado, não exercitado com hardware).
- Microfones Bluetooth (perfil de chamada) e o headset G435.
