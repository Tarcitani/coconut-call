# ADR 0001 — Monorepo

Data: 2026-11-16 · Status: aceito

## Contexto
Cliente WinUI, servidor de sinalização, contratos compartilhados, bindings nativos, instalador e ferramentas evoluem juntos: uma mudança de protocolo toca contratos, servidor e cliente ao mesmo tempo.

## Decisão
Um repositório (`C:\Dev\CoconutCall`), solução `Coconut.slnx`, projetos em `src/`, testes em `tests/`, dependências nativas em `native/`, documentação em `docs/`. Versão única do produto.

## Consequências
- Mudanças de contrato são atômicas e testadas juntas.
- CI precisa de filtros por caminho para não compilar a UI a cada mudança de servidor.
- Rejeitado: múltiplos repositórios — exigiria versionar e publicar os contratos separadamente sem benefício para um projeto deste tamanho.
