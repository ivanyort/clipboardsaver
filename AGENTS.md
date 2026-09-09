# Instruções para trabalhar no Clipboard Saver

## Projeto e arquitetura

- Leia o [README.md](README.md) para instalação, uso, arquitetura e limitações conhecidas; mantenha esses detalhes nele.
- Desenvolva e compile no WSL com C# e .NET 10. A aplicação Windows Forms executa no Windows x64 e lê o clipboard do Windows.
- Mantenha as regras portáveis em `src/ClipboardSaver.Core`, sem dependências de Windows Forms ou APIs Windows.
- Concentre a integração nativa, a bandeja e a inicialização no login em `src/ClipboardSaver.Windows`.
- Preserve a leitura do clipboard em thread STA e as gravações serializadas em segundo plano. Não bloqueie a interface durante a gravação.
- Mantenha a interface e a documentação de uso em português.

## Regras de trabalho

- Corrija a causa raiz com soluções definitivas, sem contornos. Priorize qualidade sobre rapidez.
- Restrinja as alterações ao escopo solicitado, salvo quando outra mudança for necessária para corrigir a causa raiz.
- Antes de alterações grandes, explique o plano e aguarde aprovação, exceto para correções óbvias e localizadas. Considere a aprovação já fornecida na tarefa.
- Atualize a documentação relevante quando mudar uso, instalação, configuração, API, comportamento ou arquitetura.

## Comportamento que deve ser preservado

- Uma nova cópia intencional deve gerar outro PNG, mesmo quando os pixels forem iguais aos da imagem anterior.
- Atualizações de formatos, metadados ou finalização OLE da mesma imagem publicada não devem gerar arquivos duplicados. Preserve os testes de regressão desse comportamento.
- Não substitua a identificação da publicação por deduplicação global de conteúdo ou por um intervalo de tempo que descarte cópias legítimas.
- Capture somente imagens diretamente disponíveis no clipboard; ignore texto, arquivos copiados pelo Explorador e o histórico do clipboard.
- Ao iniciar ou retomar, ignore o conteúdo que já estava no clipboard. Preserve transparência nos formatos compatíveis descritos no README.
- Nunca sobrescreva imagens existentes. Preserve nomes únicos, gravação por arquivo temporário e renomeação, e tratamento explícito de erros.
- Não altere silenciosamente a pasta de destino nem recrie uma pasta removida para ocultar falhas de armazenamento.
- Nos testes automatizados, use o clipboard e o Registro isolados. Não substitua o clipboard da sessão do usuário nem modifique sua inicialização real como parte de um teste.

## Validação e publicação

Execute os comandos na raiz do repositório:

| Comando | Quando usar |
| --- | --- |
| `./scripts/test.sh` | Alterações de código: testes portáveis e compilação Windows. |
| `./scripts/test-windows.sh` | Alterações na captura, imagens, integração Windows ou comportamento usado pelo host; requer interoperabilidade Windows no WSL. |
| `./scripts/publish.sh` | Entrega de um executável atualizado ou mudanças na publicação. |

- Os testes são executáveis próprios. Use os scripts acima; não considere apenas `dotnet test` como prova de execução das suítes.
- Antes de concluir, confirme que a causa raiz foi tratada e que os testes ou validações aplicáveis passaram. Valide o comportamento na prática quando possível.
- Ao corrigir um defeito, acrescente ou ajuste um teste que reproduza a falha, quando viável.
- Para mudanças exclusivamente documentais, confira os caminhos, comandos e a consistência das instruções, além de `git diff --check`; não é necessário executar novamente as suítes da aplicação.
- Se não puder executar a integração no Windows ou uma verificação visual, informe a limitação. Compilar no WSL não comprova o funcionamento do clipboard nem da interface.
- O executável publicado fica em `artifacts/win-x64/ClipboardSaver.exe`. Recompilar não atualiza uma cópia já instalada ou em execução; deixe claro qual binário foi gerado.

## Git e commits

- Faça commit e push somente quando houver ordem explícita. Quando houver ordem para commit, faça também o push após criá-lo.
- Use Conventional Commits, com título claro e descrição detalhada compatível com a mudança.
- Configure o autor como `Ivan Yort <ivan.yort@gmail.com>`.
- Inclua todos os arquivos alterados, adicionados e removidos no commit solicitado, respeitando o `.gitignore`. Não versione binários, logs de diagnóstico ou artefatos ignorados.
- Para mensagens com mais de uma linha, use obrigatoriamente `git commit -F -` com entrada padrão. Não use `git commit -m` multilinha, escapes de quebra de linha ou concatenação de strings para montar a mensagem.

No bash/WSL, use um heredoc literal:

```bash
git commit -F - <<'EOF'
tipo: título claro

Descrição da mudança e da validação realizada.
EOF
```

No PowerShell, envie uma here-string com quebras de linha reais para `git commit -F -`.

Antes de encerrar uma tarefa com commit, confirme os arquivos incluídos e removidos, a mensagem Conventional Commits, o autor correto e o push concluído.
