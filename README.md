# FineLocalization (Unity)

FineLocalization é um pacote leve e direto para projetos Unity, com foco em tornar a tradução multilíngue **simples, prática e eficiente** — inclusive em **WebGL com 2 GB de RAM**.

Através de uma planilha `.csv` com chaves e valores por idioma, o sistema permite que todos os textos do jogo sejam centralizados, gerenciados e atualizados de forma rápida — tanto durante o desenvolvimento quanto em produção.

---

## Principais funcionalidades

- 🌐 Tradução automática via planilha CSV com múltiplos idiomas
- 🔁 Atualização dinâmica em runtime (WebGL, mobile, desktop)
- 🪟 Janela Editor estilo planilha para edição rápida
- 🛠️ Suporte completo ao **TextMeshPro** (incluindo fallbacks remotos para CJK/Árabe/etc.)
- 🔑 Componente de texto localizado via **Key** (ex: `menu.start`)
- 🔀 **Modo Production / Development** — duas listas de planilhas separadas, com troca a um clique
- ✅ **Build processor** com proteção: avisa se você estiver tentando buildar Release com planilhas de Dev e oferece trocar
- 📦 **Bundle Builder dinâmico** de fontes remotas para WebGL (qualquer idioma)
- 🪄 Fallback automático quando uma chave está ausente (retorna a própria key)
- ⚡ Otimizado para **WebGL/2 GB de RAM**: parser sem alocações desnecessárias, scans únicos da cena, sem cópias defensivas de dicionário
- 🎯 **API única** (`Localization`) para ler texto, trocar idioma e saber quando está pronto
- 🪄 **Fontes remotas sem digitar mapeamento**: o Bundle Builder gera um manifesto e o runtime lê dele — nenhuma lista idioma → fonte no inspector
- 🧭 **Códigos de idioma normalizados**: aceita o que o host manda errado (`cn`, `jp`, `iw`, `esp`, `zh-Hans-CN`) e resolve contra as colunas que a planilha realmente tem

```csharp
using FineLocalization.Runtime;

Localization.OnReady += ok => BuildUI();          // pronto pra usar (late-subscriber safe)
Localization.OnLanguageChanged += RefreshLabels;  // idioma trocou

label.text = Localization.Get("menu.start");
Localization.SetLanguage("ja-jp");                // pode chamar a qualquer momento
```

---

## Para quem é este pacote?

- Projetos Unity que precisam de localização multilíngue
- Times que querem evitar texto hardcoded
- Times que precisam alterar textos **em produção sem rebuild**
- Builds **WebGL com restrição de memória**

---

## Instalação

### Via UPM (Git)

**Window → Package Manager → + → Add package from git URL...**

```
https://github.com/NoTaskStudios/com.notask.finelocalization.git
```

### Importação manual

Você também pode baixar o repositório e copiar para a pasta `Packages/` do seu projeto.

---

## Visão geral do menu

Tudo fica em **`Tools → Fine Localization`**:

```
Tools/Fine Localization/
├── Setup and Update                        ← COMECE AQUI: checklist do pipeline inteiro
├── Open Localization Editor              ← janela principal (estilo planilha)
├── Language Picker                       ← troca idioma no Editor
│
├── Diagnostics/
│   ├── Run Language Code Self Check      ← valida a resolução de códigos de idioma
│   ├── Report Keys Used vs Unused
│   └── Report Keys Used vs Unused (com Cenas)
│
├── Advanced/                             ← as ações individuais, para script/CI
│   ├── Sheets/
│   │   ├── Sync from Google (Download + Characters)
│   │   ├── Regenerate Characters from Saved CSVs
│   │   └── Generate Latin Base Characters
│   ├── WebGL Remote Fonts/
│   │   ├── Open Bundle Builder Window
│   │   ├── Generate Font Assets From Characters
│   │   └── Build Bundles Now
│   ├── Open Settings Inspector
│   ├── Migrate Remote Font Loader        ← v3.1 → v3.2
│   ├── Migrate Legacy Components
│   └── Reset Settings to Defaults
│
└── Documentation
```

**`Setup and Update` é a janela que você quer.** Ela mostra o pipeline em duas fases — *Textos* e
*Fontes* — com cada passo detectando sozinho se já está pronto, e faz cada ação no lugar certo e na
ordem certa. Os itens em `Advanced/` continuam existindo para automação e para quem já tem o
costume, mas nenhum deles é necessário.

---

## Setup no Editor

### 1) Criar o asset de Settings

Na primeira vez que você abrir uma das janelas, o asset `LocalizationSettings` é criado automaticamente em `Assets/FineLocalization/Resources/`.

Você pode abri-lo direto via **Tools → Fine Localization → Open Settings Inspector**.

### 2) Escolher o Mode (Production / Development)

No Inspector do `LocalizationSettings`, há um banner colorido no topo:

- 🟢 **PRODUCTION** — usa a lista `Sources` (planilhas estáveis para release)
- 🟠 **DEVELOPMENT** — usa a lista `DevSources` (planilhas em andamento, traduções em revisão)

Clique no botão **→ Switch to Production / Development** para alternar.

> A lista ativa aparece com fundo azul e contadores `[ X sources / Y sheets / Z downloaded ]`. A lista inativa fica num foldout fechado.

### 3) Adicionar Table IDs

Em cada source da lista ativa, informe o **Table ID** da planilha do Google Sheets.

> Onde encontrar o Table ID: na URL da planilha, a parte entre `/d/` e `/edit`.

### 4) Resolver Sheets

Clique em **↺ Resolve Sheets**. O sistema busca todas as abas (sheets) disponíveis do Table ID.

### 5) Baixar as planilhas

Defina **Save Folder** (não use a pasta do package em `Packages/...`).
Recomendado: `Assets/FineLocalization/Resources/Localization/`.

Você tem duas formas de baixar:

**a) Pelo Inspector do `LocalizationSettings`** — botão **▼ Download Sheets** baixa o que estiver no mode ativo.

**b) Pelo menu** — `Tools → Fine Localization → Sheets → Sync from Google (Download + Characters)` abre um **popup de escolha**:

| Opção | Quando usar |
|-------|-------------|
| **Production (recommended)** | Default seguro — baixa as planilhas estáveis. **Use sempre antes de gerar uma build de release** |
| **Development** | Baixa as planilhas em revisão. Uma confirmação extra é exigida para evitar enganos |
| **Cancel** | Aborta sem fazer nada |

> 🛡️ **Por que Production é a opção segura?** Os CSVs baixados ficam locais (`Assets/FineLocalization/Resources/Localization/`) e entram na build. Se você baixar Development por engano e esquecer, os textos de Dev vão pra produção. O popup pede confirmação extra ao escolher Development justamente por isso, e o build processor também checa.

Para uso em CI/scripts:

```csharp
LocalizationEditorCsvSync.SyncSheetsForMode(
    LocalizationSettings.LocalizationMode.Production
);
```

---

## Modes & Build Processor

### Como funciona o switch automático

Quando você inicia um build, o `LocalizationBuildProcessor` checa o mode atual:

**Se estiver em Development:**
- Aparece um dialog com 3 opções:
  1. **Trocar para Production e continuar** (recomendado para release)
  2. **Cancelar build**
  3. **Continuar com Development** (use se intencional)
- Se for um build de **Release** (não-Development), o dialog avisa em destaque.

**Se estiver em Production:**
- Confirmação rápida ("Continuar / Cancelar").

Quando você escolhe "Trocar para Production", o asset é salvo automaticamente antes do build prosseguir.

### Validações automáticas no build

Antes de gerar a build, o processor valida:
- Existe alguma source ativa?
- Todas as sheets têm `TextAsset` (obrigatório em **WebGL**, pois não há download de build)?
- Cada CSV tem header válido (`<colunas-ignoradas>,Key,<langs...>`)?
- Há idiomas duplicados?

Se alguma checagem falhar, o build é cancelado com mensagem clara.

---

## Usando no jogo

### Localizar um TMP_Text

1. Adicione o componente `LocalizedText` (ou `LocaleComponent` para projetos legados) no GameObject que tem o `TextMeshProUGUI`.
2. Preencha o campo **Key** com a chave correspondente (ex: `menu.start`).

O texto é atualizado automaticamente quando o idioma muda.

### Via script

```csharp
using FineLocalization.Runtime;

// Buscar uma tradução
string label = Localization.Get("menu.start");

// Com formatação
string greeting = Localization.Get("greeting.user", "Valdeci");

// Trocar idioma (baixa a fonte do idioma antes de atualizar os textos, se precisar)
Localization.SetLanguage("pt-br");
```

---

## Runtime

Adicione o componente `RuntimeLocaleDownloader` em um GameObject persistente da cena inicial. Ele é a configuração da API `Localization`.

### CSV em WebGL e CORS

Em WebGL o navegador exige cabeçalho CORS na resposta. O export de planilha **pública** do Google
normalmente responde com ele, então `Allow Direct Google Download In WebGL` vem **ligado**: o
comportamento padrão é tentar baixar.

Quando o navegador bloqueia, nada quebra — o download falha, o log explica (`Provável bloqueio de
CORS…`) e o jogo segue com os CSVs embutidos no build. Por isso tentar é a opção segura.

Se o seu caso é bloqueado de verdade, há duas saídas:

- **`Csv Url Override`** apontando para um proxy/CDN com CORS. Mais confiável, e evita o erro no
  console. `{0}` = TableId, `{1}` = gid.
- **Desligar `Allow Direct Google Download In WebGL`** para nem tentar, usando só os CSVs do build.

> A v3.0 até a v3.4 desligavam essa tentativa por padrão, presumindo que o Google sempre bloqueia.
> Na prática funciona para a maioria das planilhas públicas, e desligar por padrão fazia o jogo
> shipar com tradução congelada no momento do build sem ninguém perceber.

### O download nunca espera pelo idioma

Um CSV do FineLocalization contém **todas** as colunas de idioma. Por isso o pipeline é:

```
1. carrega os CSVs         ← começa no Start(), independe do idioma
2. resolve o idioma        ← cadeia de prioridade, sempre termina em algo
3. garante a fonte
4. aplica + OnReady
```

Trocar de idioma depois é instantâneo: os dados já estão em memória e só a fonte remota (quando houver) vai à rede.

### Opções no Inspector

| Campo | O que faz |
|-------|-----------|
| `Csv Source` | **Auto** (padrão): baixa em todas as plataformas. **Remote**: sempre baixa. **Bundled**: nunca baixa |
| `Csv Url Override` | URL de proxy/CDN com CORS — `{0}` = TableId, `{1}` = gid. Vazio = export direto do Google |
| `Allow Direct Google Download In WebGL` | Ligado por padrão. Em WebGL sem proxy, tenta o export direto do Google. Planilha pública normalmente responde com CORS; falhando, o jogo cai nos CSVs embutidos |
| `Startup Language` | Força um idioma no boot, ignorando URL e sistema. Vazio = usa a cadeia |
| `Usar ?lang= da URL` | Lê `?lang=`, `?locale=`, `?culture=`, `?language=`, `?lng=` da URL de lançamento |
| `Usar idioma do sistema` | Usa o idioma do SO/navegador quando nada mais define um |
| `Fallback Language` | Idioma final da cadeia, e destino de segurança quando a fonte remota falha |
| `Font Mode` | `Auto` (usa fonte remota se houver URL + manifesto), `Latin Only` (nunca baixa) ou `Remote` (sempre tenta) |
| `Base Bundle URL` / `Game Id` | CDN das fontes remotas e o segmento por jogo. Vazio no Game Id = Product Name |
| `Rede` | Tentativas, timeout e espera entre tentativas |

### Cadeia de resolução do idioma

```
Localization.SetLanguage()  →  Startup Language  →  ?lang= da URL  →  idioma do sistema  →  Fallback
```

O inspector mostra essa cadeia montada com a sua configuração atual, então dá pra ver qual idioma vai sair no boot sem entrar em Play.

`Localization.SetLanguage()` pode ser chamado **a qualquer momento** — inclusive de um `Awake` que rode antes do downloader, ou antes da planilha terminar de baixar. O pedido fica pendente e é aplicado assim que os dados chegam.

### Normalização de códigos de idioma

Hosts e SDKs mandam código de idioma errado o tempo todo: `cn` quando queriam dizer `zh` (`cn` é
**região**), `jp` no lugar de `ja`, `iw` no lugar de `he`, `esp` no lugar de `es`. O pacote aceita
tudo isso sem configuração nenhuma.

Para cada pedido, o `LanguageCode` monta uma **escada de candidatos** e testa cada um, em ordem,
contra o que existe de verdade — as colunas das planilhas carregadas e os bundles de fonte
configurados. O primeiro candidato é **sempre** o código pedido, exatamente como veio:

| pedido | escada de candidatos |
|---|---|
| `cn` | `cn` → `zh-cn` → `zh` → `zh-sg` → `zh-my` |
| `zh-hk` | `zh-hk` → `zh-tw` → `zh-mo` → `zh` → `zh-cn` → `zh-sg` → `zh-my` |
| `iw` | `iw` → `he-il` → `he` |
| `us` | `us` → `en-us` → `en` |
| `esp` | `esp` → `es-es` → `es` |

Três consequências que valem entender:

- **Nada que já funcionava muda.** Uma planilha que tenha uma coluna literal `cn` continua sendo
  atendida por ela, porque o código cru é o primeiro candidato.
- **Chinês Tradicional nunca vira Simplificado.** `zh-hk` e `zh-mo` preferem `zh-tw`; só caem em
  `zh-cn` se não houver nenhuma coluna Tradicional.
- **A ordem das colunas no Google Sheet não decide mais nada.** Quando o código é genérico
  (`zh`, `pt`, `en`) e há várias regiões, ganha a região padrão do idioma
  (`zh` → `zh-cn`, `pt` → `pt-br`, `en` → `en-us`).

A mesma escada escolhe o **bundle de fonte**, então texto e atlas nunca divergem: o idioma é
resolvido contra as planilhas primeiro, e a fonte segue o código resolvido.

#### Language Aliases

Alguns códigos são genuinamente ambíguos — `uk` é ucraniano *e* Reino Unido, e o mesmo vale para
`ca`, `ch`, `be`, `sg` e `my`. Para esses, use **Language Aliases** no `LocalizationSettings`:

| from | to |
|---|---|
| `uk` | `en-gb` |
| `es` | `es-mx` |

Um alias vence toda regra interna, mas nunca sombreia uma coluna que exista com o nome exato do
que foi pedido. Não é necessário para `cn`, `jp`, `kr`, `br` e afins — esses já funcionam sozinhos.

#### Diagnosticar

Com `EnableLogs` ligado, um idioma que não encontra coluna nenhuma loga a escada inteira e o que
havia disponível:

```
[FineLocalization] Idioma 'cn' não existe nas planilhas. Aplicado 'en-us'.
'cn' → [cn, zh-cn, zh, zh-sg, zh-my]; nenhum candidato compatível contra [en-us, pt-br]
```

Para conferir as regras sem entrar em Play: *Tools → Fine Localization → Diagnostics →
Run Language Code Self Check*.

### Saber quando está pronto

```csharp
using FineLocalization.Runtime;

public class MyUI : MonoBehaviour
{
    private void OnEnable()
    {
        // Quem se inscreve depois do evento já ter disparado é chamado na hora.
        // Não existe janela de corrida — nada de checar flag antes.
        Localization.OnReady += OnLocalizationReady;
        Localization.OnLanguageChanged += Refresh;
    }

    private void OnDisable()
    {
        Localization.OnReady -= OnLocalizationReady;
        Localization.OnLanguageChanged -= Refresh;
    }

    private void OnLocalizationReady(bool success) => Refresh();

    private void Refresh() => label.text = Localization.Get("menu.start");
}
```

### API completa

```csharp
Localization.Get(key)                    // tradução; devolve a key se não achar
Localization.Get(key, args)              // com string.Format
Localization.Has(key)

Localization.SetLanguage(lang)           // troca de idioma
Localization.SetLanguage(lang, ok => {}) // com callback do resultado
Localization.Reload(ok => {})            // re-baixa as planilhas sem reiniciar o jogo

Localization.Language                    // idioma aplicado
Localization.AvailableLanguages          // idiomas presentes nas planilhas
Localization.IsReady                     // dados carregados e idioma aplicado
Localization.LoadSucceeded               // false = planilha ou fonte falhou

Localization.OnReady                     // Action<bool>, late-subscriber safe
Localization.OnLanguageChanged           // Action
```

---

## Remote Fonts (WebGL) — fontes sob demanda para CJK/Árabe/etc.

Idiomas com **muitos glyphs** (chinês, japonês, coreano, tailandês, árabe, hindi…) inflam o tamanho
da build se as fontes forem bundled. A solução: **bundles separados por idioma**, baixados sob demanda.

### O fluxo inteiro fica numa janela

**`Tools → Fine Localization → Setup and Update`**, fase *Fontes*. Cada passo detecta sozinho se já
está pronto:

| Passo | O que a janela faz |
|---|---|
| **Entradas de bundle** | Lê as colunas de idioma dos CSVs, marca quais precisam de fonte remota (`LanguageCode.IsLatinScript`) e cria a entrada + a pasta `RemoteFonts/<idioma>` para as que faltam. O nome do bundle espelha a coluna: `font_<coluna>`. |
| **Fontes de origem** | Um campo por idioma para arrastar o `.ttf`/`.otf`. **É a única entrada manual do pipeline.** |
| **Assar font assets** | Gera um `TMP_FontAsset` SDF estático com só os caracteres daquela coluna, mantendo o `.ttf` fora do bundle. |
| **Build dos bundles** | Empacota em `AssetBundles/WebGL/Fonts` com extensão `.ft` e grava o **manifesto**. |
| **Enviar para o CDN** | Lista os arquivos e revela a pasta. O único passo que nenhuma checagem alcança. |
| **Verificar** | Aponta as contradições silenciosas — bundle para idioma latino que nunca vai baixar, fonte assada sem glifos da coluna, bundle em disco fora do manifesto, mais de um build config no projeto. |

### O que ainda é manual

Só o `.ttf`. Tudo o mais é derivado:

- **nome do bundle** ← coluna da planilha
- **pasta** ← `RemoteFonts/<coluna>`
- **caracteres a assar** ← `characters_<coluna>.txt`, gerado no download das planilhas
- **idioma que o bundle atende** ← nome do bundle
- **nome do TMP_FontAsset** ← o asset assado na pasta

### O manifesto

`Build Bundles` grava `Assets/FineLocalization/Resources/FineLocalizationFontBundles.asset` com uma
entrada por bundle: idioma, nome do arquivo e nome da fonte. **Commite esse asset** — é por ele que o
runtime sabe que existem fontes remotas.

Até a v3.1 isso era uma lista digitada no inspector (`Remote Font Mappings`), e a URL era remontada
como `"font_" + prefixo + extensão`. O manifesto guarda o nome do arquivo **que foi construído**,
então a URL e o arquivo no CDN não podem divergir.

### Usar em runtime

No `RuntimeLocaleDownloader`, seção **Fontes**:

| Campo | O que é |
|---|---|
| `Font Mode` | `Auto` (usa remota se houver URL + manifesto), `Latin Only` (nunca baixa) ou `Remote` (sempre tenta) |
| `Base Bundle URL` | URL da pasta no CDN, ex: `https://cdn.site.com/languages/` |
| `Game Id` | Segmento por jogo na URL. Vazio = **Product Name** minúsculo e sem espaços |
| `Main Font Assets` | Fontes base do projeto que recebem a fonte remota como fallback |
| `Coverage Fallback Fonts` | Fallback **permanente** de baixa prioridade para glifos soltos (₴, ₹). Nunca removido ao trocar de idioma |

O inspector mostra a **URL final de cada idioma do manifesto**, então dá para conferir o caminho sem
entrar em Play. Com `Base Bundle URL = https://cdn.site.com/languages/`, `Game Id = trevor` e um
bundle `font_ja-jp.ft`, o download vai em
`https://cdn.site.com/languages/trevor/font_ja-jp.ft`.

A ordem é garantida por construção: o downloader **parseia as planilhas, resolve o idioma contra as
colunas, baixa a fonte desse idioma resolvido e só então troca os textos**, com um único rebuild.
Assim o atlas nunca é de um idioma e o texto de outro.

Ao trocar de idioma, a fonte remota do idioma anterior é **desinstalada** das tabelas de fallback,
então o atlas antigo não fica pendurado consumindo memória. O rebuild dos textos é feito em lotes
(`Rebuild Batch Size`, padrão 24 por frame) — sem `SetActive(false/true)`, que provocaria reflow total.

Com `Font Mode = Latin Only`, ou em `Auto` sem URL/manifesto, nada é baixado: idiomas cobertos pelas
fontes embutidas funcionam normalmente e os demais caem no `Fallback Language`, com aviso no log e
`callback(false)`. É o modo que garante nunca renderizar caractere faltando.

### Testar a fonte sem subir para o CDN

A pasta de saída do Bundle Builder (`AssetBundles/WebGL/Fonts` por padrão) fica **fora de
`Assets/`**, então não é asset do projeto e não aparece no Project window. Para testar uma fonte
recém-assada sem publicar nada, ligue **`Use Local Bundles In Editor`** em *Fontes → Teste local*:

| Campo | O que faz |
|---|---|
| `Use Local Bundles In Editor` | Lê os bundles do disco em vez do CDN. `Base Bundle URL` é ignorada |
| `Local Bundle Folder` | Pasta relativa à raiz do projeto. Tem que ser a mesma do `Output Folder` do Bundle Builder |

O inspector mostra o caminho absoluto resolvido e confere se todos os bundles do manifesto estão lá.

**No Editor, sem `Base Bundle URL` preenchida, a pasta local é usada automaticamente.** Publicar no
CDN é o passo mais lento do ciclo; exigir isso só para ver a fonte na tela no Editor não fazia
sentido. Com URL preenchida, o CDN continua sendo a origem — o automático nunca sobrepõe uma
configuração explícita.

**O efeito é compilado fora do build.** O trecho que monta a URL `file://` está dentro de
`#if UNITY_EDITOR`, então um player nunca vai apontar para arquivo local — não importa o valor
salvo na cena. Não há como esquecer isso ligado e shipar.

Dois pontos que economizam confusão:

- **AssetBundle é específico de plataforma.** O Editor só abre bundle da plataforma ativa em Build
  Settings, e estes são construídos para WebGL. Com outro target o carregamento falha; o inspector
  avisa quando a plataforma ativa não é WebGL.
- **O manifesto continua obrigatório.** É ele que diz quais idiomas têm bundle e qual arquivo pedir.
  Rodar **Build Bundles** gera os dois de uma vez, então na prática não muda nada no seu fluxo.

### Auto-detecção de script Latin (otimização)

O pacote detecta se o idioma alvo usa **script Latin** (`en`, `pt`, `es`, `fr`, `de`, `it`, `nl`,
`sv`, `pl`, `cs`, `tr`, `id`, `vi`, etc. — 50+ prefixos, incluindo o código custom `ba-id` do Bahasa
Indonésia). Quando for, ele **não faz request, não baixa bundle e não força rebuild** — só devolve
sucesso na hora, partindo do princípio de que as fontes do seu projeto já cobrem Latin + Latin
Extended.

Script explícito vence a tabela: `sr-latn` conta como Latin mesmo com `sr` fora dela, e `ku-arab`
não conta como Latin mesmo com `ku` dentro. Região mandada no campo de idioma também é reinterpretada
— `us` vira `en`, então não exige uma fonte remota que ninguém publicou.

Para ajustar, em **Fontes → Avançado**:

| Campo | O que faz |
|-------|-----------|
| `Extra Latin Prefixes` | Prefixos extras a tratar como Latin — não baixam bundle |
| `Force Remote Font Prefixes` | **Override**: força download mesmo para idioma Latin. Use se sua fonte padrão é minimalista e não tem acentos completos (ex: `tr`, `vi`) |

> A tabela de prefixos Latin vive num único lugar (`LanguageCode`).

> ⚠️ Criar um bundle para um idioma latino sem adicionar o código em `Force Remote Font Prefixes`
> faz o bundle nunca ser baixado. O passo **Verificar** do Hub aponta exatamente isso.

Os `characters_<coluna>.txt` ficam em `Assets/FineLocalization/Editor/GeneratedCharacters/`. Como
estão numa pasta `Editor`, não entram na build.

---

## Configuração extra: Skip Columns

Algumas planilhas têm colunas extras antes da `Key` (ID interno, comentários, metadados).
Para ignorá-las, configure **Skip** no `LocalizationSettings`.

### Exemplo

| ID | Tipo | Key        | pt-br   | en-us |
|----|------|------------|---------|-------|
| 01 | UI   | menu.start | Iniciar | Start |
| 02 | UI   | menu.exit  | Sair    | Exit  |

Existem 2 colunas extras antes de `Key` → **`Skip = 2`**.

---

## Configuração extra: EnableLogs

No `LocalizationSettings` há um toggle **Enable Logs**.

- **Em desenvolvimento**: mantenha ligado para ver warnings/erros.
- **Em release/WebGL**: **desligue** para silenciar todos os logs do FineLocalization globalmente — economiza CPU e reduz string allocations no console.

---

## Performance / WebGL com 2 GB de RAM

Boas práticas já aplicadas no pacote (você não precisa fazer nada extra):

- `Regex` cacheado como `static readonly` no parser CSV
- `Regex.Replace` com `MatchEvaluator` (single pass) em vez de N `Replace()` no texto inteiro
- `StringBuilder` para concatenação em loops (parser e substituições CJK)
- Sem cópias defensivas de dicionário em `LoadFromCsvMap`
- **Trocar de idioma é O(1)**: os CSVs são parseados uma vez só; a troca apenas reaponta o dicionário. Na v2 cada troca re-parseava todas as planilhas
- **Um único `OnLanguageChanged` por troca**: a fonte é baixada e instalada *antes* de notificar, em vez de disparar um evento por etapa
- A instalação de fonte faz **uma única varredura da cena** por operação, deduplicando fontes via `HashSet`
- Rebuild em lotes, sem `SetActive(false/true)` — só `ForceMeshUpdate`
- Fonte remota do idioma anterior é desinstalada na troca — o atlas antigo não fica retido
- Logs gateados por `EnableLogs` via `FineLocalizationLogger` (zero-alloc quando off)
- Build processor exige TextAssets bundled em WebGL — evita falhas em runtime quando o CDN está fora

### Recomendações de projeto

1. **Desligue `EnableLogs`** nas builds de release
2. Use **`Csv Source = Auto`** — baixa em todas as plataformas, com fallback automático para os CSVs embutidos quando o download falha
3. Use **Remote Fonts** para idiomas com muitos glyphs
4. Mantenha as planilhas **enxutas** — uma chave por linha; evite valores vazios desnecessários
5. Sincronize as planilhas no Editor antes de todo build de release

---

## Migração v3.1 → v3.2 (BREAKING)

Duas mudanças estruturais. A primeira exige rodar um migrador; a segunda é só ganho.

### 1. `RemoteFontBundleLoader` deixou de existir

O componente foi absorvido pelo `RuntimeLocaleDownloader`. Eram dois MonoBehaviours que se
procuravam por `FindFirstObjectByType`, negociavam ordem por um hack (`IgnoreNextLocalizationChanged`)
e cada um instalava delegates globais no TextMeshPro que ninguém limpava.

**O que fazer, na ordem:**

1. Atualizar o pacote. Cenas e prefabs que tinham o loader vão mostrar **"missing script"**. É esperado.
2. `Tools ▸ Fine Localization ▸ Advanced ▸ Migrate Remote Font Loader` → **Escanear** → **Migrar**.
   O migrador lê a configuração antiga direto do arquivo da cena/prefab, escreve no downloader,
   liga `Font Mode = Remote` e limpa o componente órfão.
3. `Tools ▸ Fine Localization ▸ Setup and Update` → fase *Fontes* → **Build bundles**, para gerar o
   manifesto (ver abaixo).
4. Commitar `Assets/FineLocalization/Resources/FineLocalizationFontBundles.asset`.

> O migrador precisa de cena/prefab em **serialização de texto** (`Force Text` ou `Mixed`, o default
> da Unity). Em projeto com `Force Binary` ele avisa e você reconfigura `Base Bundle URL` e as listas
> de fonte à mão.

**Novo campo `Font Mode`:**

| Modo | Comportamento |
|---|---|
| `Auto` (default) | Usa fonte remota quando há `Base Bundle URL` **e** bundles no manifesto. Sem uma das duas coisas, se comporta como `Latin Only`. |
| `Latin Only` | Nunca baixa nada. Só as fontes embutidas — o único caminho garantido de abrir o jogo sem caractere faltando. Idioma que precisar de mais cai no `Fallback Language`, com aviso no log e `callback(false)`. |
| `Remote` | Sempre tenta a fonte remota do manifesto. |

**API que mudou de lugar:**

| v3.1 | v3.2 |
|---|---|
| `RemoteFontBundleLoader.IsReady` | `Localization.FontReady` |
| `RemoteFontBundleLoader.LastLanguage` | `Localization.Language` |
| `RemoteFontBundleLoader.OnFontReady` | `Localization.OnFontReady` |
| `RemoteFontBundleLoader.GetDefaultGameId()` | `RuntimeLocaleDownloader.GetDefaultGameId()` |

Os apelidos que já estavam `[Obsolete]` na v3.1 (`OnDownloadRemoteFontComplete`,
`IsRemoteFontReady`, `LastRemoteFontSucceeded`, `LastRemoteFontLanguage`,
`ShouldLoadRemoteFontForLanguage`, `IsSameLanguageOrRoot`) foram removidos.
Quem usa só `Localization` não muda nada.

### 2. `Remote Font Mappings` acabou

A lista que exigia digitar prefixo e nome do TMP_FontAsset por idioma **não existe mais**, e não
precisa ser migrada: ela nunca carregava informação nova. O prefixo saía do nome do bundle e o nome
da fonte saía do asset assado — e o runtime nem precisava dele, porque já carregava todos os assets
do bundle e pegava a primeira fonte.

Agora o **Build Bundles grava um manifesto** (`Resources/FineLocalizationFontBundles.asset`) com
idioma, nome do arquivo e nome da fonte, tudo derivado dos artefatos que realmente saíram. O runtime
lê dele.

Ganho colateral: a URL passa a usar **o nome do arquivo construído**, não uma remontagem
`"font_" + prefixo + extensão`. Um bundle fora da convenção antes gerava 404 que só aparecia em Play.

Esse asset **precisa ser commitado** — sem ele o jogo não sabe que existem fontes remotas.

### 3. Cinco arquivos do Editor que nunca compilaram

`Editor/` na raiz do pacote não tinha `.asmdef`, e pacote UPM exige um para compilar. Então overlay
de idioma na SceneView, forçar idioma ao entrar em Play, relatório de chaves usadas/não usadas,
`Reset Settings`, `Documentation` e o migrador de componentes legados **nunca existiram** para quem
consome via UPM. Os arquivos foram movidos para `Scripts/Editor/` e agora funcionam.

---

## Migração v3.0 → v3.1

Compatível: nenhuma assinatura pública mudou e todo código que já resolvia certo continua
resolvendo igual. Duas mudanças de comportamento, ambas em casos que antes eram indefinidos:

| Situação | v3.0 | v3.1 |
|---|---|---|
| Código genérico com várias regiões (`pt` com colunas `pt-pt` e `pt-br`) | a primeira coluna da planilha | a região padrão do idioma (`pt-br`) |
| Código com script explícito (`ku-arab`, `sr-latn`) | classificado pelo prefixo do idioma | classificado pelo script |

A primeira é uma correção: na v3.0 **reordenar uma coluna no Google Sheet podia mudar o idioma
do jogador**, porque o resultado vinha da ordem de enumeração do dicionário. Para forçar outra
região, use **Language Aliases**.

`RemoteFontBundleLoader.LastLanguage` passa a reportar o código resolvido em vez do pedido cru
(`zh-cn` em vez de `cn`).

## Migração v2 → v3

A v3 é **breaking**, mas os componentes continuam nos mesmos arquivos: as cenas não perdem referência e a configuração dos campos que sobreviveram é preservada. O que muda:

### API

| v2 | v3 |
|----|----|
| `RuntimeLocaleDownloader.SetRequestedLanguage(lang)` | `Localization.SetLanguage(lang)` |
| `RuntimeLocaleDownloader.OnAllSheetsDownloadedComplete` | `Localization.OnReady` |
| `RuntimeLocaleDownloader.OnDownloadLocalizationComplete` | `Localization.OnReady` |
| `RuntimeLocaleDownloader.IsLocalizationReady` | `Localization.IsReady` |
| `RuntimeLocaleDownloader.LastLocalizationSucceeded` | `Localization.LoadSucceeded` |
| `LocalizationManager.Localize(key)` | `Localization.Get(key)` |
| `LocalizationManager.OnLocalizationChanged` | `Localization.OnLanguageChanged` |
| `RemoteFontBundleLoader.IsRemoteFontReady` | `RemoteFontBundleLoader.IsReady` |
| `RemoteFontBundleLoader.OnRemoteFontDownloadComplete` | `RemoteFontBundleLoader.OnFontReady` |

Os nomes antigos continuam funcionando marcados como `[Obsolete]` — o projeto compila, com aviso indicando o substituto. `LocalizationManager` segue público e funcional; `Localization` é apenas a fachada recomendada.

### Campos do Inspector removidos

| Campo v2 | O que fazer |
|----------|-------------|
| `downloadOnStart` / `useLocalSheet` | `Csv Source`: **Bundled** para o antigo `false`, **Auto** para `true` |
| `allowDirectGoogleDownloadInWebGL` | Mesmo nome, mesmo comportamento — voltou na v3.5 ligado por padrão |
| `csvUrlPatternOverride` | Renomeado para `Csv Url Override` |
| `waitForExplicitRequestedLanguage` | **Removido** — era a causa do travamento; o download não espera mais por idioma |
| `requestedLanguageWaitTimeoutSeconds` | **Removido** pelo mesmo motivo |
| `acceptLocalizationManagerLanguageAsRequested` | **Removido** — `LocalizationManager.Language = x` sempre funciona |
| `loadRemoteFontBeforeApplyingLocalization` | **Removido** — implícito quando há um loader |
| `initialLanguageOverride` | Renomeado para `Startup Language` |
| `delayBetweenSheets` | **Removido** |
| `ignoredFontNameContains` (loader) | **Removido** — o loader identifica as próprias fontes pelo mapeamento configurado |
| `testLanguage` (loader) | Menu de contexto **Fine Localization/Recarregar fonte do idioma atual** |

> ⚠️ Se o seu jogo dependia de `waitForExplicitRequestedLanguage = true` para não abrir em inglês antes do idioma chegar do host, use `Localization.SetLanguage()` normalmente — a troca depois do boot é instantânea porque os dados já estão em memória. Se ainda quiser segurar a UI, espere pelo callback: `Localization.SetLanguage(lang, ok => ShowUI())`.

---

## Migração de pacote antigo

Se você usava o pacote `com.notask.simplelocalization` (legacy):

**Tools → Fine Localization → Migrate Legacy Components**

A janela varre o projeto e converte automaticamente os `LocaleComponent` antigos para os novos.

---

## Licença

Veja `LICENSE` no repositório.

---

## Links

- 🐛 [Issues](https://github.com/NoTaskStudios/com.notask.finelocalization/issues)
- 📦 [Releases](https://github.com/NoTaskStudios/com.notask.finelocalization/releases)
