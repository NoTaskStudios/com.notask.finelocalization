
# FineLocalization (Unity)

FineLocalization é um pacote leve e direto para projetos Unity, com foco em tornar a tradução multilíngue simples, prática e eficiente.

Através de uma planilha `.csv` com chaves e valores por idioma, o sistema permite que todos os textos do jogo sejam centralizados, gerenciados e atualizados de forma rápida — tanto durante o desenvolvimento quanto em produção.

---

## Principais funcionalidades

- Tradução automática via planilha CSV com múltiplos idiomas  
- Atualização dinâmica em runtime (ideal para WebGL, mobile e desktop)  
- Importação/atualização manual via Editor com ferramenta no menu **Tools**  
- Suporte completo ao **TextMeshPro**  
- Componente de texto localizado via **Key** (ex: `menu.start`)  
- Fallback automático quando uma chave está ausente  
- Suporte a múltiplas fontes/tabelas (ex: diálogos, menus, sistema etc.)  
- Evento para atualizar UI automaticamente quando o idioma muda:
  ```csharp
  Action OnLocalizationChanged = () => { };


---

## Para quem é este pacote?

* Projetos Unity que precisam de localização multilíngue
* Desenvolvedores que querem evitar duplicação de texto hardcoded
* Equipes que precisam de flexibilidade para alterar textos em produção (runtime)
---

## Instalação

### Via UPM (Git)

No Unity, abra:
**Window → Package Manager → + → Add package from git URL...**

Cole:

```
https://github.com/NoTaskStudios/com.notask.finelocalization.git
```

### Importação manual

<img width="415" height="114" alt="5d4798df-a692-4bb8-91e8-2b484d5f6a4f" src="https://github.com/user-attachments/assets/eeb0bc14-fc19-4d12-b74b-c1113d22ec90" /> 



Você também pode baixar o repositório e importar no projeto (caso prefira).

---

## Setup no Editor

### 1) Abrir o menu e definir Table IDs

Vá até:
**Tools → Fine Localization**

No painel, encontre **Table IDs** e informe o(s) identificador(es) das tabelas que deseja usar no projeto.

Você pode usar mais de um Table ID, por exemplo:

* Planilha 1: regras
* Planilha 2: erros

> Onde encontrar o Table ID:
<img width="606" height="30" alt="277aec4f-947b-4d13-8211-c6fe1370f6f1" src="https://github.com/user-attachments/assets/16eb8a81-6c46-4213-9bde-2d5c74085d7f" />

<img width="449" height="132" alt="a4522aab-5575-4030-9822-738d4bdf688c" src="https://github.com/user-attachments/assets/007d82b1-5f4b-4e92-806d-6be95d197db6" />

---

### 2) Resolver Sheets

Clique em **Resolve Sheets**.

Isso busca automaticamente todas as planilhas disponíveis para os Table IDs fornecidos.
Depois disso, selecione manualmente quais planilhas pertencem a este projeto.

---

### 3) Baixar as planilhas

Escolha a pasta onde as planilhas serão baixadas.

**Importante:** não baixe na pasta padrão:

* `Packages/Fine Localization/Resources/Localization`

Depois, clique em **Download**.

As planilhas selecionadas serão baixadas e armazenadas localmente no projeto, prontas para uso pelo sistema de localização.

---

## Usando no jogo

### Traduzir textos com LocaleComponent

Para cada elemento de texto (ex: `TextMeshProUGUI`) que precisa de tradução:

1. Adicione o componente `LocaleComponent.cs`
2. Preencha o campo **Key** com a chave correspondente da planilha

Exemplo:

* `Key: menu.start`

O sistema atualizará automaticamente o texto de acordo com o idioma ativo.

> Também é possível usar por script (dependendo do seu fluxo).

---

## Runtime (WebGL / Mobile / Desktop)

### Configurar o RuntimeDownloader

Adicione um GameObject vazio na cena e anexe:

* `LocaleRuntimeDownloader.cs`

Nele você define se o download das tabelas deve ser feito:

* ✅ Automaticamente no `Start()` quando o jogador abre o jogo
* 🛠️ Manualmente, apenas no Editor, quando o desenvolvedor decidir atualizar

---

## Configuração extra: Skip Columns

Algumas planilhas podem ter colunas adicionais antes de `Key` (ex: ID interno, comentários, metadados).
Para ignorar essas colunas, o FineLocalization permite configurar **Skip**, que define quantas colunas devem ser ignoradas antes da coluna `Key`.

Essa configuração fica em:

* `Resources/LocalizationSettings.asset`

Durante o carregamento, o sistema ignora automaticamente as colunas especificadas.

### Exemplo prático

| ID | Tipo | Key        | pt-BR   | en-US |
| -- | ---- | ---------- | ------- | ----- |
| 01 | UI   | menu.start | Iniciar | Start |
| 02 | UI   | menu.exit  | Sair    | Exit  |

Nesse caso, existem **duas** colunas extras antes de `Key` (`ID` e `Tipo`), então:

* `Skip = 2`

---

## Configuração extra: Forçar linguagem no Editor

Durante o desenvolvimento, é útil testar diferentes idiomas direto no Editor, sem alterar arquivos externos.

Para isso existe o **Force Editor Language** (apenas para Play Mode no Editor).

### Como usar

Vá até:
**Tools → Fine Localization → Settings**

1. Encontre **Force Editor Language**
2. Selecione o idioma desejado (ex: `pt-BR`, `en-US`)
3. Dê Play: o idioma será aplicado imediatamente

> Isso não afeta builds em produção. Serve apenas para testes no Editor.
