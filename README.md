# Clipboard Saver

Aplicativo para Windows que salva automaticamente imagens da área de transferência em uma pasta escolhida. Roda em segundo plano, com ícone na bandeja, e pode ser desenvolvido e compilado no WSL.

## Usar no Windows

1. Copie `artifacts/win-x64/ClipboardSaver.exe` para uma pasta permanente do Windows, por exemplo `%LOCALAPPDATA%\Programs\ClipboardSaver`.
2. Abra o executável. Ele inclui o runtime .NET e não exige instalação do SDK, privilégios de administrador ou WSL para funcionar.
3. Escolha a pasta de destino na primeira execução. A captura começa depois da escolha; cancelar mantém o aplicativo pausado.
4. Faça uma captura com `Win + Shift + S` ou use **Copiar imagem** em um aplicativo. A imagem aparecerá na pasta como PNG.

Procure o ícone de prancheta na bandeja, inclusive na área de ícones ocultos. Clique com o botão direito para acessar:

| Ação | Comportamento |
| --- | --- |
| Escolher pasta | Altera e memoriza o destino; uma escolha confirmada ativa a captura. |
| Abrir pasta | Abre o destino no Explorador; também disponível por duplo clique no ícone. |
| Pausar / Retomar captura | Controla a captura de novas imagens. |
| Iniciar com o Windows | Ativa ou remove a inicialização no login do usuário atual. Desativada inicialmente. |
| Ver último erro | Mostra os detalhes do último problema ocorrido nesta execução. |
| Sair | Para novas capturas, conclui as gravações já aceitas e encerra o aplicativo. |

Os arquivos recebem data e hora locais: `2026-09-09_14-35-22-123.png`. Se o nome já existir, são usados sufixos `_1`, `_2`, etc. Nenhum arquivo existente é sobrescrito. Copiar a mesma imagem novamente gera outro arquivo.

Somente imagens disponíveis diretamente no clipboard são capturadas. Texto, links e arquivos copiados pelo Explorador são ignorados. Não há consulta ao histórico `Win + V`. O conteúdo que já existia ao abrir ou retomar o aplicativo não é salvo. Gravações aceitas antes de pausar ou trocar de pasta terminam no destino original.

PNG é priorizado; bitmap e DIBV5 também são aceitos. Transparência é preservada em PNG e DIBV5 de 32 bits com máscara alfa explícita padrão. Outros bitmaps usam a conversão GDI do Windows, que pode não fornecer transparência.

## Desenvolvimento no WSL

Requisitos: SDK .NET 10, acesso à internet no primeiro restore e Windows x64 para execução. A solução usa C# e Windows Forms, sem pacotes externos de interface ou captura. `global.json` aceita SDKs estáveis 10.0 posteriores à versão base.

Se o SDK ainda não estiver instalado:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/clipboard-dotnet-install.sh
bash /tmp/clipboard-dotnet-install.sh --channel 10.0 \
  --install-dir "$HOME/.local/share/clipboard-dotnet"
```

Os scripts procuram `dotnet` no `PATH` e, em seguida, no diretório acima. Para usar a CLI diretamente, adicione esse diretório ao seu `PATH`.

Na raiz do projeto:

```bash
# Testes portáveis e compilação da aplicação Windows
./scripts/test.sh

# Executável autossuficiente para Windows x64
./scripts/publish.sh

# Testes de integração reais no Windows, iniciados pelo WSL
./scripts/test-windows.sh
```

A publicação fica em `artifacts/win-x64/ClipboardSaver.exe`. Copie esse arquivo para o Windows e abra-o pelo Explorador. O Windows pode extrair bibliotecas do runtime para sua pasta temporária na primeira execução.

`EnableWindowsTargeting` permite [compilar projetos Windows no Linux](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100). A aplicação continua sendo um processo Windows: não lê o clipboard Linux/WSLg e não depende de polling ou de uma ponte PowerShell. A [publicação em arquivo único](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) inclui o runtime; trimming permanece desativado para compatibilidade com Windows Forms.

## Arquitetura e tratamento de falhas

- `src/ClipboardSaver.Core`: controle de captura, cancelamento e repetição de leituras, fila de gravação, nomes únicos e configurações JSON. Não depende de Windows Forms.
- `src/ClipboardSaver.Windows`: interface da bandeja, integração nativa com clipboard, imagens, registro de inicialização e logs.
- `tests`: executáveis de teste que retornam código diferente de zero quando há falha, sem dependências de frameworks externos.

Uma janela oculta se inscreve em `AddClipboardFormatListener` e recebe `WM_CLIPBOARDUPDATE`. A leitura ocorre na thread STA, sob `OpenClipboard`, copiando os dados antes de liberar o clipboard. O número de sequência elimina notificações repetidas da mesma atualização. Quando há um bitmap nativo, sua identidade (`HBITMAP`) também identifica a imagem publicada: metadados, disponibilização tardia de formatos e `OleFlushClipboard` podem mudar a sequência sem representar outra cópia. Essas atualizações não geram outro PNG. Uma nova cópia cria outro objeto de imagem e continua sendo salva, mesmo com pixels idênticos; não há deduplicação por conteúdo ou intervalo de tempo. Para fontes que oferecem somente PNG, sem bitmap nativo, o controle utiliza a sequência do clipboard.

Clipboard ocupado causa até cinco novas tentativas, distribuídas por aproximadamente um segundo. Uma cópia mais recente cancela a tentativa anterior. Se a leitura continuar indisponível, um aviso pede para copiar a imagem novamente e o monitoramento continua.

Codificação PNG e gravação são serializadas em segundo plano. Primeiro é escrito um arquivo temporário na própria pasta; após o flush ele é renomeado sem sobrescrever o destino. Falha de gravação pausa a captura, descarta as imagens ainda na fila e mostra um aviso. Corrija a pasta e retome pelo menu; imagens que não foram salvas precisam ser copiadas novamente. O aplicativo não muda silenciosamente o destino nem recria pastas removidas.

A fila aceita até 32 imagens pendentes, além da gravação em andamento. Se esse limite for atingido, novas capturas são pausadas com aviso e as gravações já aceitas terminam. O clipboard é um único conteúdo mutável: imagens substituídas antes que o Windows permita sua leitura não podem ser recuperadas por este aplicativo.

## Configurações, logs e remoção

Os dados do aplicativo ficam em `%LOCALAPPDATA%\ClipboardSaver`:

- `settings.json`: caminho absoluto da pasta de destino, salvo de forma atômica.
- `clipboard-saver.log`: erros; há rotação com um arquivo anterior quando o log ultrapassa aproximadamente 512 KiB. Imagens e conteúdo do clipboard não são registrados no log.

Uma configuração ilegível gera aviso e solicita novamente a pasta. Pasta indisponível mantém a captura pausada. A pausa é temporária: ao reabrir com uma pasta válida, o aplicativo volta a capturar novas imagens.

A inicialização automática usa o valor `ClipboardSaver` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, com o caminho do executável entre aspas. O Registro é a fonte dessa preferência. Se mudar o executável de lugar, desative e ative novamente a opção a partir da nova localização. Bloqueios da inicialização pelo Gerenciador de Tarefas ou por políticas do Windows precisam ser tratados no próprio Windows.

Para remover, desmarque **Iniciar com o Windows**, saia pelo menu e exclua o executável. Se desejar, exclua também a pasta de configurações. As imagens permanecem na pasta escolhida. Caso o executável já tenha sido removido, exclua somente o valor `ClipboardSaver` da chave de Registro indicada acima.

## Validação

Os testes portáveis cobrem sequência e identidade da imagem publicada, repetição intencional, pausa, clipboard ocupado, cancelamento de tentativas antigas, renderização atrasada, fila, erro de gravação, colisões de nomes, limpeza de arquivos parciais e persistência de configurações. Há regressões no Windows para metadados adicionados após a captura, PNG disponibilizado depois do bitmap e finalização via `OleFlushClipboard`, incluindo uma nova cópia intencional da mesma imagem.

Os testes Windows usam uma **window station e um desktop isolados**: executam as APIs nativas sem ler ou substituir o clipboard da sessão do usuário. Também verificam bitmaps, PNG e DIBV5 com alfa, cópias rápidas, pastas ausentes, negação real de escrita por ACL e inicialização do host da bandeja. O teste de autostart redireciona HKCU apenas no processo de teste para uma chave temporária; não altera a inicialização real do usuário. O relatório fica em `artifacts/windows-test-results.txt`.

Para executar a integração diretamente no Windows, publique o projeto `tests/ClipboardSaver.Windows.Tests` com `dotnet publish -c Release -r win-x64 --self-contained true` e execute o `.exe` gerado. A opção `--results caminho.txt` salva o relatório.

Ainda faça esta verificação visual ao instalar na sua sessão:

1. Confirme seleção da pasta, menu da bandeja, pausa/retomada e abertura do Explorador.
2. Copie imagens pelo aplicativo de recorte, navegador e Paint e confira os PNGs.
3. Reabra para confirmar a pasta lembrada; abra uma segunda vez para confirmar o aviso de instância existente.
4. Ative a inicialização automática e confirme o funcionamento no próximo login; depois desative se não desejar mantê-la.

Os testes automatizados não encerram sua sessão, não abrem navegador/Paint e não comprovam a aparência do ícone na bandeja da sua sessão interativa.
