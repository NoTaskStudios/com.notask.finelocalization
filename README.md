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
├── Open Localization Editor              ← janela principal (estilo planilha)
├── Open Settings Inspector               ← abre o ScriptableObject de Settings
├── Language Picker (Preview)             ← troca idioma no Editor
│
├── Sheets/
│   ├── Sync from Google (Download + Characters)
│   ├── Regenerate Characters from Saved CSVs
│   └── Generate Latin Base Characters
│
├── WebGL Remote Fonts/
│   ├── Open Bundle Builder Window
│   ├── Generate Font Assets From Characters
│   └── Build Bundles Now
│
├── Diagnostics/
│   └── Run Language Code Self Check       ← valida a resolução de códigos de idioma
│
├── Migrate Legacy Components             ← migra componentes do pacote antigo
├── Reset Settings to Defaults
└── Documentation
```

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
| `Csv Source` | **Auto** (padrão): baixa em Editor/Desktop/Mobile; em WebGL usa os CSVs embutidos, porque o Google Sheets responde ao export sem cabeçalho CORS. **Remote**: sempre baixa. **Bundled**: nunca baixa |
| `Csv Url Override` | URL de proxy/CDN com CORS — `{0}` = TableId, `{1}` = gid. Preenchido, liga o download também em WebGL no modo Auto |
| `Startup Language` | Força um idioma no boot, ignorando URL e sistema. Vazio = usa a cadeia |
| `Usar ?lang= da URL` | Lê `?lang=`, `?locale=`, `?culture=`, `?language=`, `?lng=` da URL de lançamento |
| `Usar idioma do sistema` | Usa o idioma do SO/navegador quando nada mais define um |
| `Fallback Language` | Idioma final da cadeia, e destino de segurança quando a fonte remota falha |
| `Remote Font Loader` | (opcional) Vazio = procura um `RemoteFontBundleLoader` na cena, inclusive inativo |
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

Idiomas com **muitos glyphs** (chinês, japonês, coreano, tailandês, árabe, hindi…) inflam o tamanho da build se as fontes forem bundled. A solução: **bundles separados por idioma**, baixados sob demanda.

### Configurar bundles

1. **Tools → Fine Localization → WebGL Remote Fonts → Open Bundle Builder Window**
2. Na primeira vez, o asset `RemoteFontBundleBuildConfig` é criado automaticamente em `Assets/FineLocalization/Editor/`.
   O pacote também cria a estrutura inicial de fontes dentro do FineLocalization:

   ```
   Assets/
   └── FineLocalization/
       └── RemoteFonts/
           ├── ChineseSimplified/
           ├── ChineseTraditional/
           ├── Japanese/
           ├── Korean/
           └── Thai/
   ```

3. Para cada idioma:
   - Defina **Bundle name** sem extensão (ex: `font_ar`, `font_he`, `font_vi`)
   - Arraste a **pasta** que contém os `TMP_FontAsset` desse idioma
   - A janela valida e mostra `✔ N TMP_FontAsset(s) em '...'`
4. Clique em **▶ Build Bundles**.
   Os arquivos são gerados em `AssetBundles/WebGL/Fonts` com extensão `.ft` (ex: `font_ja-jp.ft`), e o log lista o tamanho de cada um.

> A lista é totalmente dinâmica — adicione/remova quantos idiomas quiser. **Nada é hardcoded.**

Ao sincronizar as planilhas, o pacote gera arquivos de caracteres em
`Assets/FineLocalization/Editor/GeneratedCharacters/`, como `characters_all.txt`,
`characters_ja.txt`, `characters_ko.txt` e `characters_th.txt`.
O sufixo segue o nome da coluna de idioma no CSV.
Para criar o `TMP_FontAsset` remoto há duas opções:
- **Automático (recomendado):** defina a **Source Font** (.ttf) de cada idioma na janela e clique em **⚙ Generate Font Assets** (ou menu *Tools → Fine Localization → WebGL Remote Fonts → Generate Font Assets From Characters*). Ele assa um SDF estático contendo só os caracteres do `characters_<lang>.txt`, salva na pasta do idioma e mantém o `.ttf` **fora** do bundle. Settings de atlas/point size/padding ficam na janela.
- **Manual:** use o arquivo do idioma correspondente no Font Asset Creator.

Qualquer um dos dois evita que uma fonte japonesa inclua glyphs de coreano, tailandês, moedas ou outros idiomas.
O arquivo por idioma contém apenas caracteres encontrados naquela coluna; caracteres comuns de runtime
ficam no arquivo agregado/base.
Como esses `.txt` ficam em pasta `Editor`, eles não entram na build.

### Usar em runtime

Adicione o componente `RemoteFontBundleLoader` na cena e configure:
- `Base Bundle URL` — URL da pasta no CDN, ex: `https://cdn.site.com/languages/`
- `Game Id` — segmento por jogo na URL. Vazio = usa o **Product Name** do Player Settings (minúsculo, sem espaços). Ex: `trevor`
- `Bundle Extension` — mantenha `.ft` para os bundles gerados pelo builder
- `Remote Font Mappings` — prefixo de idioma + nome exato do TMP_FontAsset dentro do AssetBundle
- `Main Local TMP Fonts` — fontes base do projeto que recebem a fonte remota como fallback
- `Coverage Fallback Fonts` — fallback **permanente** de baixa prioridade para glifos soltos (₴, ₹) que a fonte principal não tem. Nunca removido ao trocar de idioma

Com `Base Bundle URL = https://cdn.site.com/languages/`, `Game Id = trevor` e `languagePrefix = ja`,
o loader baixa `https://cdn.site.com/languages/trevor/font_ja-jp.ft`.

Havendo um `RuntimeLocaleDownloader` na cena, é ele quem comanda o loader — na ordem certa: **fonte primeiro, textos depois**, com um único rebuild. Sem downloader, o loader se vira sozinho reagindo a `OnLanguageChanged`.

Ao trocar de idioma, a fonte remota do idioma anterior é **desinstalada** das tabelas de fallback, então o atlas antigo não fica pendurado consumindo memória.

O rebuild dos textos é feito em lotes (`Rebuild Batch Size`, padrão 24 por frame) — sem `SetActive(false/true)`, que provocaria reflow total.

Para testar um idioma em Play: menu de contexto do componente → **Fine Localization/Recarregar fonte do idioma atual**.

### Auto-detecção de script Latin (otimização)

O loader detecta automaticamente se o idioma alvo usa **script Latin** (`en`, `pt`, `es`, `fr`, `de`, `it`, `nl`, `sv`, `pl`, `cs`, `tr`, `id`, `vi`, etc. — 50+ prefixos cobertos, incluindo o código custom `ba-id` do Bahasa Indonésia). Quando for, ele:

- ✅ **Não faz request HTTP** (sem rede)
- ✅ **Não baixa AssetBundle** (sem alocação/cache de bundle)
- ✅ **Não força rebuild dos textos** (sem stall de UI)
- ✅ Apenas retorna `onComplete(true)` instantaneamente

Isso parte do princípio que **as fontes padrão do seu projeto já cobrem Latin + Latin Extended** (incluindo acentos `áéíóú`, `ç`, `ñ`, etc.) — o que é verdade pra 99% dos projetos Unity.

### Quando ajustar a auto-detecção?

No Inspector do `RemoteFontBundleLoader`, no header **Advanced**:

| Campo | O que faz |
|-------|-----------|
| `Extra Latin Prefixes` | Prefixos extras a tratar como Latin (ex: `tlh`, `eo`) — não baixam bundle |
| `Force Remote Font Prefixes` | **Override**: força download mesmo para idiomas Latin. Use se sua fonte padrão é minimalista e não tem acentos completos (ex: `tr`, `vi`) |

> A tabela de prefixos Latin vive num único lugar (`LanguageCode`), compartilhada pelo downloader e pelo loader.

> 💡 Idiomas **não-Latin** (CJK, Árabe, Hebraico, Tailandês, Devanagari, Cirílico, Grego, etc.) **sempre** caem no fluxo de download — só procuram bundle se você tiver criado config pra eles no `RemoteFontBundleBuildConfig`.

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
- `RemoteFontBundleLoader` faz **uma única varredura da cena** por operação, deduplicando fontes via `HashSet`
- Rebuild em lotes, sem `SetActive(false/true)` — só `ForceMeshUpdate`
- Fonte remota do idioma anterior é desinstalada na troca — o atlas antigo não fica retido
- Logs gateados por `EnableLogs` via `FineLocalizationLogger` (zero-alloc quando off)
- Build processor exige TextAssets bundled em WebGL — evita falhas em runtime quando o CDN está fora

### Recomendações de projeto

1. **Desligue `EnableLogs`** nas builds de release
2. Use **`Csv Source = Auto`** — ele já escolhe bundled em WebGL e remoto nas demais plataformas
3. Use **Remote Fonts** para idiomas com muitos glyphs
4. Mantenha as planilhas **enxutas** — uma chave por linha; evite valores vazios desnecessários
5. Sincronize as planilhas no Editor antes de todo build de release

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
| `allowDirectGoogleDownloadInWebGL` | `Csv Source = Remote` + `Csv Url Override` |
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
