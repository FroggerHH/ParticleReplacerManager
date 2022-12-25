using HarmonyLib;
using JetBrains.Annotations;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

#nullable enable

#pragma warning disable CS1591
#pragma warning disable CS8604
namespace ParticleReplacerManager
{
    public static class ParticleReplacer
    {
        /// <summary>
        /// Whether to change the shredder of ALL smoke particles to Custom/LitParticles.
        /// </summary>
        public static bool fixSmoke = true;

        static ParticleReplacer()
        {
            _objectsForShaderReplace = new();
            Harmony harmony = new("org.bepinex.helpers.ParticleReplacer");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)),
                prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ParticleReplacer),
                    nameof(ReplaceAllMaterialsWithOriginal))));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Smoke), nameof(Smoke.Awake)),
                postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ParticleReplacer),
                    nameof(SmokeFix))));
        }

        private static readonly List<GOSnapData> _objectsForShaderReplace;

        /// <summary>
        /// Registration of the prefab of the particle system/particle system inside it to replace the material shader.
        /// </summary>
        /// <param name="go"> Prefab of a ParticleSystem or any other prefab having them inside itself. </param>
        /// <param name="shaderType"> The shader to be applied to the material of the particles. </param>
        /// <param name="transformPath"> The path where the particulars are located inside the prefab.
        /// Leave it unchanged if the ParticleSystem component is on the prefab itself.
        /// Example: "_enabled/flames". </param>
        public static void RegisterParticleSystemForShaderSwap(GameObject go, ShaderType shaderType, string transformPath = "this object")
        {
            _objectsForShaderReplace?.Add(new() { gameObject = go, shader = shaderType, transformPath = transformPath });
        }
        /// <summary>
        /// Registration of the prefab of the particle system (the particle system inside it)  to replace the material shader. 
        /// In case it is inside the AssetBundle and for some reason you don't want to download it yourself.
        /// </summary>
        /// <param name="assetBundleName"> The name of the AssetBundle inside which the prefab is located. </param>
        /// <param name="prefabName"> The name of the prefab, inside the AssetBundle. </param>
        /// <param name="folderName"> The folder inside which the AssetBundle is located in the project.
        /// By default, "assets". </param>
        /// <param name="shaderType"> The shader to be applied to the material of the particles. </param>
        /// <param name="transformPath"> The path where the particulars are located inside the prefab. 
        /// Leave it unchanged if the ParticleSystem component is on the prefab itself.
        /// Example: "_enabled/flames". </param> 
        public static void RegisterParticleSystemForShaderSwap(string assetBundleName, string prefabName, ShaderType shaderType, string transformPath = "this object", string folderName = "assets")
        {
            GameObject? go = RegisterPrefab(assetBundleName, prefabName, folderName);
            if(!go)
            {
                Debug.LogError($"Can't find GameObject with name {prefabName}. AssetBundle {assetBundleName} exists, but it doesn't have {prefabName} in it. Perhaps this is an automatically added prefab, in which case you need to manually add it to the AssetBundle.");
                return;
            }
            RegisterParticleSystemForShaderSwap(go, shaderType, transformPath);
        }
        /// <summary>
        /// Registration of the prefab of the particle system (the particle system inside it)  to replace the material shader. 
        /// In case it is inside the AssetBundle and for some reason you don't want to download it yourself.
        /// </summary>
        /// <param name="assetBundle"> AssetBundle inside which the prefab is located. </param>
        /// <param name="prefabName"> The name of the prefab, inside the AssetBundle. </param>
        /// <param name="shaderType"> The shader to be applied to the material of the particles. </param>
        /// <param name="transformPath"> The path where the particulars are located inside the prefab. 
        /// Leave it unchanged if the ParticleSystem component is on the prefab itself.
        /// Example: "_enabled/flames". </param>
        public static void RegisterParticleSystemForShaderSwap(AssetBundle assetBundle, string prefabName, ShaderType shaderType, string transformPath = "this object")
        {
            RegisterParticleSystemForShaderSwap(RegisterPrefab(assetBundle, prefabName), shaderType, transformPath);
        }

        private static AssetBundle RegisterAssetBundle(string assetBundleFileName, string folderName = "assets")
        {
            BundleId id = new() { assetBundleFileName = assetBundleFileName, folderName = folderName };
            if(!bundleCache.TryGetValue(id, out AssetBundle assets))
            {
                assets = bundleCache[id] =
                    Resources.FindObjectsOfTypeAll<AssetBundle>().FirstOrDefault(a => a.name == assetBundleFileName) ??
                    AssetBundle.LoadFromStream(Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream(Assembly.GetExecutingAssembly().GetName().Name + $".{folderName}." +
                                                   assetBundleFileName));
            }

            return assets;
        }
        private struct BundleId
        {
            [UsedImplicitly] public string assetBundleFileName;
            [UsedImplicitly] public string folderName;
        }
        private static readonly Dictionary<BundleId, AssetBundle> bundleCache = new();
        private static GameObject? RegisterPrefab(string assetBundleFileName, string prefabName, string folderName = "assets")
        {
            AssetBundle assets = RegisterAssetBundle(assetBundleFileName, folderName);
            if(!assets)
            {
                Debug.LogError($"Can't find AssetBundle with name {assetBundleFileName}.");
                return null;
            }
            return RegisterPrefab(assets, prefabName);
        }
        private static GameObject? RegisterPrefab(AssetBundle assets, string prefabName)
        {
            GameObject gameObject = assets.LoadAsset<GameObject>(prefabName);
            return gameObject;
        }

        [HarmonyPriority(Priority.VeryHigh)]
        private static void ReplaceAllMaterialsWithOriginal()
        {
            if(SceneManager.GetActiveScene().name != "start") return;
            foreach(GOSnapData? gOSnapData in _objectsForShaderReplace)
            {
                if(gOSnapData.gameObject == null)
                {
                    Debug.LogError($"GameObject for Particle Material is null");
                    return;
                }
                if(gOSnapData.transformPath != "this object")
                {
                    List<string> paths = gOSnapData.transformPath.Split('/').ToList();
                    foreach(string item in paths)
                    {
                        Transform transform = gOSnapData.gameObject.transform.Find(item);
                        if(!transform)
                        {
                            Debug.LogError($"Can't Find child with name {item} in {gOSnapData.gameObject.name}");
                            return;
                        }
                        gOSnapData.gameObject = transform.gameObject;
                    }
                }
                ParticleSystemRenderer particleSystem = gOSnapData.gameObject.GetComponent<ParticleSystemRenderer>();
                if(!particleSystem)
                {
                    Debug.LogError($"Can't Find Component ParticleSystemRenderer on {gOSnapData.gameObject.name}");
                    return;
                }

                ShaderType shaderType = gOSnapData.shader;
                foreach(Material? t in particleSystem.materials)
                {
                    string name = t.shader.name;
                    t.shader = shaderType switch
                    {
                        ShaderType.AlphaParticle => Shader.Find("Custom/AlphaParticle"),
                        ShaderType.LitParticles => Shader.Find("Custom/LitParticles"),
                        ShaderType.ShadowBlob => Shader.Find("Custom/ShadowBlob"),
                        ShaderType.ParticleDecal => Shader.Find("Custom/ParticleDecal"),
                        ShaderType.LuxLitParticlesBumped => Shader.Find("LuxLitParticles/Bumped"),
                        ShaderType.UseUnityShader => Shader.Find(name),
                        _ => Shader.Find("Custom/LitParticles"),
                    };
                }
            }
        }
        [HarmonyPriority(Priority.VeryHigh)]
        private static void SmokeFix(Smoke __instance)
        {
            if(fixSmoke) __instance.GetComponent<Renderer>().material.shader = Shader.Find("Custom/LitParticles");
        }

        public enum ShaderType
        {
            AlphaParticle,
            LitParticles,
            ParticleDecal,
            ShadowBlob,
            LuxLitParticlesBumped,
            LuxLitParticlesTessBumped,
            UseUnityShader
        }
        private class GOSnapData
        {
#pragma warning disable CS8618
            [NotNull] public GameObject gameObject;
            public string transformPath = "this object";
            public ShaderType shader = ShaderType.UseUnityShader;
        }
    }
}

