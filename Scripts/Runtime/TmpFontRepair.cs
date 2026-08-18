using System;
using System.Collections.Generic;
using System.Reflection;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;

namespace FineLocalization.Scripts.Runtime
{
    /// <summary>
    /// Conserta TMP_FontAssets vindos de AssetBundle.
    ///
    /// Um TMP_FontAsset serializado num bundle perde a ligação com seu material: o material
    /// referenciado não vem junto, ou vem apontando para um atlas que não existe mais. Sem
    /// reparo, o TMP renderiza nada — ou lança exceção dentro de
    /// <c>TMP_MaterialManager.GetFallbackMaterial</c>. Esta classe recria o material a partir do
    /// atlas real e reescreve as uniforms de decodificação SDF com as métricas com que o atlas
    /// foi assado, sem o que o texto sai borrado ou invisível.
    /// </summary>
    internal static class TmpFontRepair
    {
        /// <summary>
        /// Fontes locais válidas usadas como molde ao clonar um material. Definido pelo
        /// <see cref="RuntimeLocaleDownloader"/> a partir das suas Main Fonts.
        /// </summary>
        internal static IList<TMP_FontAsset> MaterialTemplates { get; set; }

        /// <summary>
        /// Diz se um font asset é gerenciado pelo pacote (veio de bundle remoto). Só nesses o
        /// reparo agressivo — recriar material e reescrever métricas SDF — é aplicado; fontes do
        /// projeto nunca são alteradas.
        /// </summary>
        internal static Func<TMP_FontAsset, bool> IsRemoteFont { get; set; } = _ => false;

        private static readonly HashSet<Material> RuntimeMaterials = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            MaterialTemplates = null;
            IsRemoteFont = _ => false;
            RuntimeMaterials.Clear();
            ClearFallbackMaterialCache();
        }

        // ------------------------------------------------------------------ Consulta

        /// <summary>True quando a fonte tem atlas e um material que aponta para esse atlas.</summary>
        internal static bool IsUsable(TMP_FontAsset font)
        {
            var atlas = GetAtlas(font);
            return atlas != null && IsUsableMaterial(GetMaterial(font), atlas);
        }

        /// <summary>Material da fonte, ou null. Nunca lança — um asset destruído devolve null.</summary>
        internal static Material GetMaterial(TMP_FontAsset font)
        {
            if (font == null)
                return null;

            try
            {
                var material = font.material;
                return material != null ? material : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Textura de atlas da fonte, tolerando as duas formas que o TMP usa.</summary>
        internal static Texture GetAtlas(TMP_FontAsset font)
        {
            if (font == null)
                return null;

            var atlas = font.atlasTexture;
            if (atlas == null && font.atlasTextures != null && font.atlasTextures.Length > 0)
                atlas = font.atlasTextures[0];

            return atlas;
        }

        /// <summary>Descrição compacta para mensagens de log.</summary>
        internal static string Describe(TMP_FontAsset font)
        {
            if (font == null)
                return "NULL";

            var material = GetMaterial(font);
            var atlas = GetAtlas(font);

            return $"{font.name} | mat={(material != null ? material.name : "NULL")} " +
                   $"| atlas={(atlas != null ? atlas.name : "NULL")} | chars={font.characterTable?.Count ?? 0}";
        }

        // ------------------------------------------------------------------- Reparo

        /// <summary>
        /// Deixa a fonte em estado renderizável. Devolve o resultado de <see cref="IsUsable"/>.
        /// <paramref name="bundleMaterial"/> é o material que veio junto no AssetBundle, quando houver.
        /// </summary>
        internal static bool Repair(TMP_FontAsset font, Material bundleMaterial = null)
        {
            if (font == null)
                return false;

            font.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var atlas = GetAtlas(font);
            if (atlas != null)
                atlas.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var isRemote = atlas != null && SafeIsRemote(font);
            var current = GetMaterial(font);

            // Fonte remota ainda sem material próprio: clona um molde e aponta pro atlas real.
            if (isRemote && !RuntimeMaterials.Contains(current))
            {
                var runtime = CloneMaterial(font, atlas, bundleMaterial, "material do bundle")
                              ?? CloneFromLocalTemplate(font, atlas);

                if (runtime != null)
                {
                    font.material = runtime;
                    return IsUsable(font);
                }
            }

            if (TryPointToAtlas(current, atlas))
            {
                current.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                if (isRemote)
                    ApplySdfMetrics(current, font);

                return IsUsable(font);
            }

            if (TryPointToAtlas(bundleMaterial, atlas))
            {
                if (isRemote)
                {
                    var runtime = CloneMaterial(font, atlas, bundleMaterial, "material do bundle");
                    if (runtime != null)
                    {
                        font.material = runtime;
                        return IsUsable(font);
                    }
                }
                else
                {
                    bundleMaterial.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    RuntimeMaterials.Add(bundleMaterial);
                    font.material = bundleMaterial;
                    return IsUsable(font);
                }
            }

            if (atlas == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] '{font.name}' não tem atlas. Material não pode ser reparado.");
                return false;
            }

            var fallbackMaterial = CloneFromLocalTemplate(font, atlas);
            if (fallbackMaterial != null)
                font.material = fallbackMaterial;
            else
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Nenhum material TMP base disponível para reparar '{font.name}'.");

            return IsUsable(font);
        }

        /// <summary>Reexecuta a leitura da definição da fonte — o bundle não a traz pronta.</summary>
        internal static void ReadDefinition(TMP_FontAsset font)
        {
            if (font == null)
                return;

            try
            {
                font.ReadFontAssetDefinition();
            }
            catch (Exception ex)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] ReadFontAssetDefinition falhou em '{font.name}': {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        /// <summary>Impede que a Unity descarregue assets de bundle que ainda estão em uso.</summary>
        internal static void KeepAlive(UnityEngine.Object[] assets, ICollection<UnityEngine.Object> tracker)
        {
            if (assets == null)
                return;

            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null)
                    continue;

                asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                tracker?.Add(asset);
            }
        }

        internal static void KeepAtlasesAlive(TMP_FontAsset font)
        {
            if (font == null)
                return;

            if (font.atlasTexture != null)
                font.atlasTexture.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            if (font.atlasTextures == null)
                return;

            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                if (font.atlasTextures[i] != null)
                    font.atlasTextures[i].hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
        }

        /// <summary>
        /// Descarta o cache de materiais de fallback do TMP. Obrigatório após mexer em qualquer
        /// tabela de fallback: o cache guarda combinações material+atlas que passam a estar erradas.
        /// </summary>
        internal static void ClearFallbackMaterialCache()
        {
            try
            {
                var type = Type.GetType("TMPro.TMP_MaterialManager, Unity.TextMeshPro");
                var method = type?.GetMethod("ClearFallbackMaterials", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                method?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] ClearFallbackMaterials falhou: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------------------------------------------------------- Materiais do texto

        /// <summary>
        /// Alinha as uniforms SDF dos materiais de fallback já instanciados por um TMP_Text com
        /// as métricas da fonte remota. Sem isso, os sub-meshes gerados pelo TMP herdam a
        /// GradientScale da fonte principal e os glifos remotos saem com peso errado.
        /// </summary>
        internal static void NormalizeTextMaterials(TMP_Text text, TMP_FontAsset remoteFont)
        {
            var remoteMaterial = GetMaterial(remoteFont);
            var remoteAtlas = GetAtlas(remoteFont);
            if (text == null || remoteMaterial == null || remoteAtlas == null)
                return;

            try
            {
                var shared = text.fontSharedMaterials;
                if (shared != null)
                {
                    for (int i = 0; i < shared.Length; i++)
                        NormalizeOne(shared[i], remoteFont, remoteMaterial, remoteAtlas);
                }
            }
            catch
            {
                // Algumas versões do TMP lançam ao reconstruir o array de materiais no meio do frame.
            }

            if (text is not TextMeshProUGUI uiText)
                return;

            var subMeshes = uiText.GetComponentsInChildren<TMP_SubMeshUI>(true);
            for (int i = 0; i < subMeshes.Length; i++)
            {
                if (subMeshes[i] != null)
                    NormalizeOne(subMeshes[i].sharedMaterial, remoteFont, remoteMaterial, remoteAtlas);
            }
        }

        private static void NormalizeOne(Material material, TMP_FontAsset remoteFont, Material remoteMaterial, Texture remoteAtlas)
        {
            if (material == null)
                return;

            Texture mainTex;
            try
            {
                mainTex = material.GetTexture(ShaderUtilities.ID_MainTex);
            }
            catch
            {
                return;
            }

            // Só toca em materiais que de fato renderizam o atlas remoto.
            if (mainTex != remoteAtlas)
                return;

            ApplySdfMetrics(material, remoteFont);
            CopyFloat(remoteMaterial, material, "_ScaleX");
            CopyFloat(remoteMaterial, material, "_ScaleY");
            CopyFloat(remoteMaterial, material, "_PerspectiveFilter");
            CopyFloat(remoteMaterial, material, "_WeightNormal");
            CopyFloat(remoteMaterial, material, "_WeightBold");
        }

        // ------------------------------------------------------------------ Interno

        /// <summary>
        /// Reescreve as uniforms de decodificação SDF com as métricas do atlas da fonte, para que
        /// o threshold usado ao renderizar bata com o usado ao assar o atlas.
        /// <c>_GradientScale = atlasPadding + 1</c> é o raio de busca SDF em texels.
        /// </summary>
        private static void ApplySdfMetrics(Material material, TMP_FontAsset font)
        {
            if (material == null || font == null)
                return;

            try
            {
                if (font.atlasWidth > 0)
                    SetFloat(material, "_TextureWidth", font.atlasWidth);

                if (font.atlasHeight > 0)
                    SetFloat(material, "_TextureHeight", font.atlasHeight);

                if (font.atlasPadding >= 0)
                    SetFloat(material, "_GradientScale", font.atlasPadding + 1);
            }
            catch (Exception ex)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Métricas SDF de '{font.name}' falharam: {ex.Message}");
            }
        }

        private static Material CloneMaterial(TMP_FontAsset font, Texture atlas, Material template, string origin)
        {
            if (font == null || atlas == null || !HasUsableShader(template))
                return null;

            var material = UnityEngine.Object.Instantiate(template);
            material.name = font.name + " Runtime Material";
            material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            material.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            ApplySdfMetrics(material, font);
            RuntimeMaterials.Add(material);
            ClearFallbackMaterialCache();

            FineLocalizationLogger.Log(
                () => $"[FineLocalization] Material runtime criado para '{font.name}' a partir de {origin} '{template.name}'."
            );

            return material;
        }

        private static Material CloneFromLocalTemplate(TMP_FontAsset font, Texture atlas)
        {
            var template = FindTemplateMaterial(font);
            if (template != null)
                return CloneMaterial(font, atlas, template, "molde local");

            var shader = FindDistanceFieldShader();
            if (shader == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Shader TMP Distance Field não encontrado para '{font.name}'.");
                return null;
            }

            var material = new Material(shader) { name = font.name + " Runtime Material" };
            material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            material.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            ApplySdfMetrics(material, font);
            RuntimeMaterials.Add(material);
            ClearFallbackMaterialCache();

            return material;
        }

        private static Material FindTemplateMaterial(TMP_FontAsset exclude)
        {
            var templates = MaterialTemplates;
            if (templates != null)
            {
                for (int i = 0; i < templates.Count; i++)
                {
                    var font = templates[i];
                    if (font == null || font == exclude)
                        continue;

                    var material = GetMaterial(font);
                    if (HasUsableShader(material))
                        return material;
                }
            }

            var defaultMaterial = GetMaterial(TMP_Settings.defaultFontAsset);
            return HasUsableShader(defaultMaterial) ? defaultMaterial : null;
        }

        private static Shader FindDistanceFieldShader()
        {
            var fromDefault = GetMaterial(TMP_Settings.defaultFontAsset)?.shader;
            if (IsUsableShader(fromDefault))
                return fromDefault;

            var mobile = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (IsUsableShader(mobile))
                return mobile;

            var desktop = Shader.Find("TextMeshPro/Distance Field");
            return IsUsableShader(desktop) ? desktop : null;
        }

        private static bool TryPointToAtlas(Material material, Texture atlas)
        {
            if (!HasUsableShader(material))
                return false;

            if (atlas == null)
                return IsUsableMaterial(material, null);

            try
            {
                if (material.GetTexture(ShaderUtilities.ID_MainTex) != atlas)
                    material.SetTexture(ShaderUtilities.ID_MainTex, atlas);

                return material.GetTexture(ShaderUtilities.ID_MainTex) == atlas;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUsableMaterial(Material material, Texture expectedAtlas)
        {
            if (!HasUsableShader(material))
                return false;

            try
            {
                var mainTex = material.GetTexture(ShaderUtilities.ID_MainTex);
                return expectedAtlas == null ? mainTex != null : mainTex == expectedAtlas;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasUsableShader(Material material)
        {
            if (material == null)
                return false;

            try
            {
                return IsUsableShader(material.shader);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUsableShader(Shader shader)
        {
            return shader != null &&
                   shader.isSupported &&
                   shader.name.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool SafeIsRemote(TMP_FontAsset font)
        {
            try
            {
                return IsRemoteFont?.Invoke(font) ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static void CopyFloat(Material source, Material target, string property)
        {
            try
            {
                if (source.HasProperty(property) && target.HasProperty(property))
                    target.SetFloat(property, source.GetFloat(property));
            }
            catch
            {
                // Propriedade inexistente nesta versão do shader TMP.
            }
        }
    }
}
