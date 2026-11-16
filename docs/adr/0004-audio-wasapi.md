# ADR 0004 — Áudio: interop WASAPI própria, categoria Communications, sem supressão por IA

Data: 2026-11-16 · Status: aceito ([evidência P3](../benchmarks/2026-11-16-P3-audio-local.md))

## Contexto
O MVP pede áudio natural com processamento mínimo, tratamento de eco, dispositivos padrão do Windows com escolha manual, troca a quente e recuperação quando um dispositivo some. Supressão de ruído por IA está fora do escopo.

## Decisão
- **Interop COM própria e mínima** (`Coconut.Audio/Interop/CoreAudio.cs`, `GeneratedComInterface`), transcrita do Windows SDK 10.0.26100 — IIDs e ordem de métodos conferidos no cabeçalho, não na documentação web (que lista `IAudioEffectsManager` em ordem alfabética, diferente da vtable).
- **WASAPI compartilhado, orientado a evento, 48 kHz mono 16 bits** com `AUTOCONVERTPCM | SRC_DEFAULT_QUALITY`: o Windows converte do formato do dispositivo; nenhum reamostrador próprio.
- **Categoria `Communications`** na captura e na reprodução: o Windows aplica o modo de VoIP, que inclui **cancelamento de eco**; a referência do AEC é apontada para a saída em uso (`IAcousticEchoCancellationControl`, build 22621+).
- **Supressão de ruído profunda (IA) desligada** via `IAudioEffectsManager::SetAudioEffectState`, aplicada **depois** de `Start` (antes, a chamada é aceita e ignorada — observado) e confirmada relendo o estado.
- Objetos COM de áudio vivem só em threads MTA (threads próprias de captura/reprodução); nunca na thread de UI.
- Mudo: o quadro capturado é descartado **antes** do codificador; o timestamp RTP continua avançando.

## Alternativas rejeitadas
- **NAudio (MIT):** só aplica `SetClientProperties` no caminho `ActivateAsync` por *device interface path*; versões publicadas irregulares (`3.4.0-preview` → `22.0.0`) pedem cautela; seria uma dependência grande para pouca superfície usada.
- **WebRTC APM (AEC3) nativo:** acrescentaria processamento próprio e build nativo extra; o AEC do sistema atende enquanto não houver evidência de qualidade insuficiente.

## Consequências e limitação conhecida
No modo Communications, o Windows também liga **supressão de ruído simples e controle automático de ganho**, sem permitir desligá-los (não ajustáveis no microfone testado). Para desligá-los seria preciso abrir mão do AEC (categoria padrão). Decisão de produto registrada em [status](../status.md).
