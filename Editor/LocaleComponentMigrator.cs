using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor.Presets;
using UnityEditor.Experimental.SceneManagement;
using UnityEditorInternal;
using TMPro;
using System.Reflection;

namespace FineLocalization.Editor
{
    public class LocaleComponentMigrator : EditorWindow
    {
        private int foundComponents = 0;
        private int migratedComponents = 0;
        private Vector2 scrollPosition;
        private bool includePrefabs = true;
        private bool includeScenes = true;
        private bool showDetails = false;

        private static readonly string[] SearchInAssets = new[] { "Assets" };
        private const string OldFullTypeName = "Localization.LocaleComponent";

        [MenuItem("Tools/Fine Localization/Migrate Simple to Fine")]
        public static void ShowWindow()
        {
            GetWindow<LocaleComponentMigrator>("Locale Component Migrator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Locale Component Migrator", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Migra componentes 'Localization.LocaleComponent' para 'FineLocalization.Runtime.LocaleComponent', " +
                "mantendo as keys e o TMP_Text, varrendo apenas a pasta Assets/.",
                MessageType.Info
            );

            GUILayout.Space(10);

            includePrefabs = EditorGUILayout.Toggle("Incluir Prefabs (Assets/)", includePrefabs);
            includeScenes  = EditorGUILayout.Toggle("Incluir Cenas (Assets/)", includeScenes);
            showDetails    = EditorGUILayout.Toggle("Mostrar Detalhes", showDetails);

            GUILayout.Space(10);

            if (GUILayout.Button("Escanear Componentes", GUILayout.Height(30)))
            {
                ScanForComponents();
            }

            if (foundComponents > 0)
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox($"Encontrados {foundComponents} componentes para migrar.", MessageType.Warning);

                if (GUILayout.Button("Migrar Todos os Componentes", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Confirmar Migração",
                        $"Tem certeza que deseja migrar {foundComponents} componentes?\n\n" +
                        "Recomendado fazer backup/commit antes. Esta ação alterará prefabs/cenas.",
                        "Migrar", "Cancelar"))
                    {
                        MigrateAllComponents();
                    }
                }
            }

            if (migratedComponents > 0)
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox($"[FineLocalization] Migração concluída! {migratedComponents} componentes foram migrados.", MessageType.Info);
            }
        }

        private void ScanForComponents()
        {
            foundComponents = 0;

            //Debug.log("[FineLocalization] Escaneando componentes antigos (Localization.LocaleComponent) apenas em Assets/");

            if (includePrefabs)
            {
                foundComponents += ScanPrefabs();
            }

            if (includeScenes)
            {
                foundComponents += ScanScenes();
            }

            //Debug.log($"[FineLocalization] Varredura concluída: {foundComponents} componentes encontrados em Assets/.");
        }

        private int ScanPrefabs()
        {
            int count = 0;
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", SearchInAssets);

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // Usar a instância carregada para contagem (sem editar aqui)
                Component[] comps = prefab.GetComponentsInChildren<Component>(true);
                foreach (Component comp in comps)
                {
                    if (comp != null && comp.GetType().FullName == OldFullTypeName)
                    {
                        count++;
                        if (showDetails){
                            //Debug.log($"[FineLocalization] (Scan) Prefab: {path} -> {comp.gameObject.name}");
                        }
                    }
                }
            }

            if (showDetails) //Debug.log($"[FineLocalization] Prefabs: {count} componentes encontrados (Assets/)");
            return count;
        }

        private int ScanScenes()
        {
            int count = 0;
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", SearchInAssets);

            // Vamos abrir ADDITIVE para não fechar a cena atual do usuário
            foreach (string guid in sceneGuids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    Component[] comps = root.GetComponentsInChildren<Component>(true);
                    foreach (var comp in comps)
                    {
                        if (comp != null && comp.GetType().FullName == OldFullTypeName)
                        {
                            count++;
                            if (showDetails){
                                //Debug.log($"[FineLocalization] (Scan) Cena: {scenePath} -> {comp.gameObject.name}");
                            }
                        }
                    }
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            if (showDetails) //Debug.log($"[FineLocalization] Cenas: {count} componentes encontrados (Assets/)");
            return count;
        }

        private void MigrateAllComponents()
        {
            migratedComponents = 0;

            //Debug.log("[FineLocalization] Iniciando migração (somente Assets/)");

            if (includePrefabs)
            {
                migratedComponents += MigratePrefabs();
            }

            if (includeScenes)
            {
                migratedComponents += MigrateScenes();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            //Debug.log($"[FineLocalization] Migração concluída: {migratedComponents} componentes migrados.");
        }

        private int MigratePrefabs()
        {
            int count = 0;
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", SearchInAssets);

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Abre o conteúdo do prefab para edição segura
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null) continue;

                bool modified = false;

                var comps = root.GetComponentsInChildren<Component>(true);
                // Cuidado: modificar enquanto itera pode invalidar; então coletar primeiro
                var toMigrate = new System.Collections.Generic.List<Component>();
                foreach (var comp in comps)
                {
                    if (comp != null && comp.GetType().FullName == OldFullTypeName)
                    {
                        toMigrate.Add(comp);
                    }
                }

                foreach (var oldComp in toMigrate)
                {
                    if (ReplaceLocaleComponent(oldComp))
                    {
                        count++;
                        modified = true;
                    }
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    if (showDetails || true){
                        //Debug.log($"[FineLocalization] Prefab migrado: {path}");
                    }
                }

                PrefabUtility.UnloadPrefabContents(root);
            }

            return count;
        }

        private int MigrateScenes()
        {
            int count = 0;
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", SearchInAssets);

            foreach (string guid in sceneGuids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                // Use Single para carregar só a cena de trabalho durante a migração
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                bool sceneModified = false;

                var roots = scene.GetRootGameObjects();
                var toMigrate = new System.Collections.Generic.List<Component>();

                foreach (var root in roots)
                {
                    Component[] comps = root.GetComponentsInChildren<Component>(true);
                    foreach (var comp in comps)
                    {
                        if (comp != null && comp.GetType().FullName == OldFullTypeName)
                        {
                            toMigrate.Add(comp);
                        }
                    }
                }

                foreach (var oldComp in toMigrate)
                {
                    if (ReplaceLocaleComponent(oldComp))
                    {
                        count++;
                        sceneModified = true;
                    }
                }

                if (sceneModified)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    if (showDetails || true){
                        //Debug.log($"[FineLocalization] Cena migrada: {scenePath}");
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Substitui um componente Localization.LocaleComponent por FineLocalization.Runtime.LocaleComponent
        /// preservando 'key' (string) e 'text' (TMP_Text).
        /// Remove apenas o componente antigo e adiciona o novo no mesmo GameObject.
        /// </summary>
        private static bool ReplaceLocaleComponent(Component oldComponent)
        {
            if (oldComponent == null) return false;

            try
            {
                // 1) Ler dados do antigo via reflexão
                var oldType = oldComponent.GetType();
                var keyFi  = oldType.GetField("key",  BindingFlags.NonPublic | BindingFlags.Instance);
                var textFi = oldType.GetField("text", BindingFlags.NonPublic | BindingFlags.Instance);

                string key = keyFi != null ? (string)keyFi.GetValue(oldComponent) : null;
                TMP_Text text = textFi != null ? (TMP_Text)textFi.GetValue(oldComponent) : null;

                var go = oldComponent.gameObject;

                // 2) Registrar Undo — útil para desfazer se necessário
                if (PrefabUtility.IsPartOfPrefabAsset(oldComponent))
                    Undo.RegisterFullObjectHierarchyUndo(go, "Migrate LocaleComponent (Prefab)");
                else
                    Undo.RegisterCompleteObjectUndo(go, "Migrate LocaleComponent (Scene)");

                // 3) Remover APENAS o componente antigo (não o GameObject)
                //    Em LoadPrefabContents, false é suficiente (é uma instância temporária).
                Object.DestroyImmediate(oldComponent, /*allowDestroyingAssets:*/ false);

                // 4) Adicionar o novo componente
                var newComp = go.AddComponent<FineLocalization.Runtime.LocaleComponent>();

                // 5) Reatribuir valores privados no novo
                var newType = newComp.GetType();
                var nKeyFi  = newType.GetField("key",  BindingFlags.NonPublic | BindingFlags.Instance);
                var nTextFi = newType.GetField("text", BindingFlags.NonPublic | BindingFlags.Instance);

                if (nKeyFi  != null) nKeyFi.SetValue(newComp, key);
                if (nTextFi != null && text != null) nTextFi.SetValue(newComp, text);

                // 6) Marcar objeto alterado
                EditorUtility.SetDirty(go);

                //Debug.log($"[FineLocalization] Migrado: {go.name} (key: '{key}')");
                return true;
            }
            catch (System.Exception e)
            {
                //Debug.logError($"[FineLocalization] Erro ao migrar componente em {oldComponent.gameObject.name}: {e.Message}");
                return false;
            }
        }
    }
}
