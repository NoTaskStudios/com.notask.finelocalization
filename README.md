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
- 📢 Eventos para reagir a download e troca de idioma:
  ```csharp
  LocalizationManager.OnLocalizationChanged           // idioma trocou
  RuntimeLocaleDownloader.OnDownloadLocalizationComplete   // download por source terminou
  RuntimeLocaleDownloader.OnAllSheetsDownloadedComplete    // ciclo completo terminou
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
│   └── Build Bundles Now
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
string label = LocalizationManager.Localize("menu.start");

// Com formatação
string greeting = LocalizationManager.Localize("greeting.user", "Valdeci");

// Trocar idioma
LocalizationManager.Language = "pt-br";
```

---

## Runtime: download + bundled CSVs (WebGL/Mobile/Desktop)

Adicione o componente `RuntimeLocaleDownloader` em um GameObject persistente da cena.

### Opções no Inspector

| Campo | O que faz |
|-------|-----------|
| `downloadOnStart` | Se **true**, baixa as planilhas no `Start()`. Se **false**, usa direto os TextAssets bundled e dispara os eventos como se tivesse baixado |
| `allowDirectGoogleDownloadInWebGL` | Em WebGL, Google Sheets bloqueia por CORS. Mantenha **false** a menos que seu deploy confirme que funciona |
| `csvUrlPatternOverride` | URL alternativa (proxy/CDN com CORS) — use `{0}` para TableId e `{1}` para gid |
| `maxDownloadAttempts` | Quantas tentativas por sheet |
| `requestTimeoutSeconds` | Timeout HTTP |
| `retryDelaySeconds` | Espera entre tentativas |
| `delayBetweenSheets` | Espera entre downloads de sheets |
| `remoteFontBundleLoader` | (opcional) Carrega bundle de fonte remoto antes de aplicar a tradução |

### Padrão recomendado para subscribers

Para que **componentes que se registram tarde** (depois do evento já ter disparado) também sejam notificados, use o padrão com a flag estática `IsLocalizationReady`:

```csharp
using FineLocalization.Scripts.Runtime;

public class MyUI : MonoBehaviour
{
    private void Awake()
    {
        RuntimeLocaleDownloader.OnAllSheetsDownloadedComplete += OnLocReady;

        // Cobre o caso de chegarmos tarde — a flag estática diz se o
        // evento já disparou antes deste Awake rodar.
        if (RuntimeLocaleDownloader.IsLocalizationReady)
            OnLocReady(RuntimeLocaleDownloader.LastLocalizationSucceeded);
    }

    private void OnDestroy()
    {
        RuntimeLocaleDownloader.OnAllSheetsDownloadedComplete -= OnLocReady;
    }

    private void OnLocReady(bool success)
    {
        if (!success) return;
        // Construa sua UI usando LocalizationManager.Localize(...)
    }
}
```

> Os eventos `OnDownloadLocalizationComplete` e `OnAllSheetsDownloadedComplete` disparam **igual** quando `downloadOnStart = false` — usando os CSVs bundled. Projetos antigos não quebram.

### Reagir a troca de idioma

```csharp
private void OnEnable()
{
    LocalizationManager.OnLocalizationChanged += Refresh;
}

private void OnDisable()
{
    LocalizationManager.OnLocalizationChanged -= Refresh;
}

private void Refresh()
{
    label.text = LocalizationManager.Localize(_key);
}
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
   - Defina **Bundle name** (ex: `font_ar`, `font_he`, `font_vi`)
   - Arraste a **pasta** que contém os `TMP_FontAsset` desse idioma
   - A janela valida e mostra `✔ N TMP_FontAsset(s) em '...'`
4. Clique em **▶ Build WebGL Bundles**.

> A lista é totalmente dinâmica — adicione/remova quantos idiomas quiser. **Nada é hardcoded.**

### Usar em runtime

Adicione o componente `RemoteFontBundleLoader` na cena e configure:
- `baseBundleUrl` — URL do CDN onde os bundles foram hospedados
- `bundles` — lista de configs (prefixo de idioma + nome do TMP_FontAsset dentro do bundle)
- `mainFontAssets` — fontes principais que recebem o fallback
- `addToGlobalTmpFallbacks` — adiciona ao TMP_Settings globalmente

O loader observa `LocalizationManager.OnLocalizationChanged` e baixa automaticamente quando o idioma muda. Faz **uma única varredura** da cena e força rebuild dos textos ativos — sem `SetActive(false/true)` (que provoca reflow total).

### Auto-detecção de script Latin (otimização)

O loader detecta automaticamente se o idioma alvo usa **script Latin** (`en`, `pt`, `es`, `fr`, `de`, `it`, `nl`, `sv`, `pl`, `cs`, `tr`, `id`, `vi`, etc. — 50+ prefixos cobertos). Quando for, ele:

- ✅ **Não faz request HTTP** (sem rede)
- ✅ **Não baixa AssetBundle** (sem alocação/cache de bundle)
- ✅ **Não força rebuild dos textos** (sem stall de UI)
- ✅ Apenas retorna `onComplete(true)` instantaneamente

Isso parte do princípio que **as fontes padrão do seu projeto já cobrem Latin + Latin Extended** (incluindo acentos `áéíóú`, `ç`, `ñ`, etc.) — o que é verdade pra 99% dos projetos Unity.

### Quando ajustar a auto-detecção?

No Inspector do `RemoteFontBundleLoader`, há um header **Script Detection (Optimization)** com:

| Campo | O que faz |
|-------|-----------|
| `skipDownloadForLatinScripts` | Master toggle. **true** (default) → otimização ligada. **false** → todo idioma com config tenta baixar |
| `extraLatinPrefixes` | Adicione prefixos extras a tratar como Latin (ex: `tlh`, `eo`) — não baixam bundle |
| `forceRemoteFontPrefixes` | **Override**: força download mesmo para idiomas Latin. Use se sua fonte padrão é minimalista e não tem acentos completos (ex: `tr`, `vi`) |

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
- `RemoteFontBundleLoader` faz **uma única varredura da cena** (com `FindObjectsByType` quando disponível), deduplicando fontes via `HashSet` estático reutilizável
- `RemoteFontBundleLoader` **não** toggla `SetActive(false/true)` em todos os textos — só `ForceMeshUpdate`
- Logs gateados por `EnableLogs` via `FineLocalizationLogger` (zero-alloc quando off)
- Build processor exige TextAssets bundled em WebGL — evita falhas em runtime quando o CDN está fora

### Recomendações de projeto

1. **Desligue `EnableLogs`** nas builds de release
2. Use **`downloadOnStart = false`** em WebGL se você bundleou os CSVs como TextAsset (mais rápido — sem rede)
3. Use **Remote Fonts** para idiomas com muitos glyphs
4. Mantenha as planilhas **enxutas** — uma chave por linha; evite valores vazios desnecessários
5. Configure `delayBetweenSheets` entre 0.05–0.1s para não saturar a stack de rede em mobile

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
