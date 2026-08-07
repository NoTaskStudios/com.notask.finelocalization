using System;
using System.Collections;
using System.Collections.Generic;
using FineLocalization.Scripts.Runtime;
using UnityEngine;

namespace FineLocalization.Runtime
{
    /// <summary>
    /// Ponto de entrada único do FineLocalization. Tudo que um jogo precisa saber está aqui:
    /// ler um texto, trocar o idioma e reagir a "está pronto".
    ///
    /// <code>
    /// Localization.OnReady += ok => BuildUI();
    /// Localization.SetLanguage("ja-jp");
    /// label.text = Localization.Get("menu.start");
    /// </code>
    ///
    /// <see cref="SetLanguage"/> pode ser chamado a qualquer momento — inclusive antes da
    /// planilha terminar de baixar, e inclusive de um <c>Awake</c> que rode antes do
    /// <see cref="RuntimeLocaleDownloader"/>. O pedido fica pendente e é aplicado assim que os
    /// dados chegam, sem travar o download: o CSV contém todos os idiomas, então baixar nunca
    /// depende de saber qual idioma será usado.
    /// </summary>
    public static class Localization
    {
        private static Action<bool> _onReady = _ => { };
        private static RuntimeLocaleDownloader _runtime;
        private static string _pendingLanguage;
        private static Action<bool> _pendingCallback;
        private static bool _deferredApplyScheduled;

        /// <summary>True quando a planilha já foi carregada e um idioma já está aplicado.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>Resultado do último ciclo de carga. False indica planilha ou fonte que falhou.</summary>
        public static bool LoadSucceeded { get; private set; }

        /// <summary>Idioma atualmente aplicado, já resolvido contra o que existe na planilha.</summary>
        public static string Language => LocalizationManager.Language;

        /// <summary>Idiomas presentes nas planilhas carregadas.</summary>
        public static IEnumerable<string> AvailableLanguages => LocalizationManager.Dictionary.Keys;

        /// <summary>
        /// Dispara quando a localização fica utilizável. Quem se inscreve depois do evento já
        /// ter ocorrido é chamado na hora — não existe janela de corrida.
        /// </summary>
        public static event Action<bool> OnReady
        {
            add
            {
                _onReady += value;
                if (IsReady)
                    value?.Invoke(LoadSucceeded);
            }
            remove => _onReady -= value;
        }

        /// <summary>Dispara toda vez que o idioma aplicado muda ou os textos são recarregados.</summary>
        public static event Action OnLanguageChanged
        {
            add => LocalizationManager.OnLocalizationChanged += value;
            remove => LocalizationManager.OnLocalizationChanged -= value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _onReady = _ => { };
            _runtime = null;
            _pendingLanguage = null;
            _pendingCallback = null;
            _deferredApplyScheduled = false;
            IsReady = false;
            LoadSucceeded = false;
        }

        /// <summary>Tradução da chave no idioma atual. Devolve a própria chave se não existir.</summary>
        public static string Get(string key) => LocalizationManager.Localize(key);

        /// <summary>Tradução com <c>string.Format</c> aplicado sobre os argumentos.</summary>
        public static string Get(string key, params object[] args) => LocalizationManager.Localize(key, args);

        /// <summary>True quando a chave existe no idioma atual.</summary>
        public static bool Has(string key) => LocalizationManager.HasKey(key);

        /// <summary>
        /// Troca o idioma. Baixa a fonte remota antes de atualizar os textos quando o idioma
        /// exigir uma. O callback recebe o resultado — false quando a fonte remota falhou e o
        /// sistema caiu no idioma de fallback.
        /// </summary>
        public static void SetLanguage(string language, Action<bool> onApplied = null)
        {
            var normalized = LanguageCode.Normalize(language);
            if (string.IsNullOrEmpty(normalized))
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] SetLanguage recebeu um código vazio. Ignorado.");
                onApplied?.Invoke(false);
                return;
            }

            _pendingLanguage = normalized;
            _pendingCallback = onApplied;

            if (_runtime != null)
            {
                _runtime.ConsumePendingLanguage();
                return;
            }

            // Ainda não há downloader — pode ser que o Awake dele simplesmente não tenha rodado
            // ainda. Esperamos um frame: se aparecer, ele assume o pedido pendente; se não
            // aparecer, o projeto é bundled-only e aplicamos direto sobre os TextAssets.
            ScheduleDeferredApply();
        }

        /// <summary>
        /// Recarrega a planilha (re-baixando quando a fonte for remota) mantendo o idioma atual.
        /// Permite corrigir traduções em produção sem reiniciar o jogo.
        /// </summary>
        public static void Reload(Action<bool> onComplete = null)
        {
            if (_runtime != null)
            {
                _runtime.Reload(onComplete);
                return;
            }

            LocalizationManager.ReloadAll();
            onComplete?.Invoke(true);
        }

        internal static void Attach(RuntimeLocaleDownloader runtime) => _runtime = runtime;

        internal static void Detach(RuntimeLocaleDownloader runtime)
        {
            if (_runtime == runtime)
                _runtime = null;
        }

        /// <summary>Retira o idioma pedido pelo jogo antes do sistema estar pronto, se houver.</summary>
        internal static bool TryTakePendingLanguage(out string language, out Action<bool> callback)
        {
            language = _pendingLanguage;
            callback = _pendingCallback;
            _pendingLanguage = null;
            _pendingCallback = null;
            return !string.IsNullOrEmpty(language);
        }

        internal static void MarkReady(bool success)
        {
            IsReady = true;
            LoadSucceeded = success;
            _onReady?.Invoke(success);
        }

        private static void ScheduleDeferredApply()
        {
            if (_deferredApplyScheduled)
                return;

            _deferredApplyScheduled = true;
            LocalizationRunner.Run(DeferredApplyWithoutRuntime());
        }

        private static IEnumerator DeferredApplyWithoutRuntime()
        {
            yield return null;
            _deferredApplyScheduled = false;

            // Um RuntimeLocaleDownloader entrou em cena nesse meio tempo: ele é o dono do
            // pipeline (CSV + fonte remota) e vai consumir o pedido pendente sozinho.
            if (_runtime != null)
            {
                _runtime.ConsumePendingLanguage();
                yield break;
            }

            if (!TryTakePendingLanguage(out var language, out var callback))
                yield break;

            if (LocalizationManager.Dictionary.Count == 0)
                LocalizationManager.Read();

            LocalizationManager.Language = language;
            MarkReady(true);
            callback?.Invoke(true);
        }
    }

    /// <summary>
    /// Host de coroutine para quando o pacote precisa de tempo mas não há nenhum componente
    /// FineLocalization na cena. Criado sob demanda, oculto na hierarquia e persistente.
    /// </summary>
    internal class LocalizationRunner : MonoBehaviour
    {
        private static LocalizationRunner _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => _instance = null;

        internal static void Run(IEnumerator routine)
        {
            if (_instance == null)
            {
                // HideInHierarchy sem DontSave: o objeto some da hierarquia mas continua sendo
                // um objeto de cena normal, então a Unity o destrói ao sair do Play Mode.
                var host = new GameObject("[FineLocalization]") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<LocalizationRunner>();
            }

            _instance.StartCoroutine(routine);
        }
    }
}
