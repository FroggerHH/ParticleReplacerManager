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
        public static bool FixSmoke = true;
        /// <summary>
        /// Whether to debug full stack of fixing ships particles materials.
        /// </summary>
        public static bool ShipFixingDebuging = false;

        static ParticleReplacer()
        {
            objectsForShaderReplace = new();
            Harmony harmony = new("org.bepinex.helpers.ParticleReplacer");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)),
                prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ParticleReplacer),
                    nameof(ReplaceAllMaterialsWithOriginal))));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ZoneSystem), nameof(ZoneSystem.Start)),
                postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ParticleReplacer),
                    nameof(FixShipsPatch))));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Smoke), nameof(Smoke.Awake)),
                postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ParticleReplacer),
                    nameof(SmokeFix))));
        }

        private static readonly List<GOSnapData> objectsForShaderReplace;
        internal static List<GameObject> ships = new();

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
            objectsForShaderReplace?.Add(new() { gameObject = go, shader = shaderType, transformPath = transformPath });
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
        /// <summary>
        /// Registration of the prefab of the ship to replace particles in it.
        /// </summary>
        /// <param name="gameObject"> Prefab of a ship. </param>
        public static void FixShip(GameObject gameObject)
        {
            ships.Add(gameObject);
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

            foreach(GOSnapData? gOSnapData in objectsForShaderReplace)
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

                switch(shaderType)
                {
                    case ShaderType.AlphaParticle:
                        foreach(Material? t in particleSystem.materials)
                        {
                            t.shader = Shader.Find("Custom/AlphaParticle");
                        }
                        break;
                    case ShaderType.LitParticles:
                        foreach(Material? t in particleSystem.materials)
                        {
                            t.shader = Shader.Find("Custom/LitParticles");
                        }
                        break;
                    case ShaderType.ParticleDecal:
                        foreach(Material? t in particleSystem.materials)
                        {
                            t.shader = Shader.Find("Custom/ParticleDecal");
                        }
                        break;
                    case ShaderType.LuxLitParticlesBumped:
                        foreach(Material? t in particleSystem.materials)
                        {
                            t.shader = Shader.Find("LuxLitParticles/Bumped");
                        }
                        break;
                    case ShaderType.UseUnityShader:
                        foreach(Material? t in particleSystem.materials)
                        {
                            string name = t.shader.name;
                            t.shader = Shader.Find(name);
                        }
                        break;
                    default:
                        foreach(Material? t in particleSystem.materials)
                        {
                            t.shader = Shader.Find("Custom/LitParticles");
                        }
                        break;
                }
            }
        }

        [HarmonyPriority(Priority.VeryHigh)]
        private static void FixShipsPatch()
        {
            GameObject karve = ZNetScene.instance.GetPrefab("Karve");
            GameObject vikingShip = ZNetScene.instance.GetPrefab("Karve");
            Ship karveShip = karve.GetComponent<Ship>();
            Piece karvePiece = karve.GetComponent<Piece>();
            WaterTrigger? karveWaterTrigger = Utils.FindChild(karve.transform, "vfx_water_surface")?.GetComponent<WaterTrigger>();
            ParticleSystemRenderer? karveVfx_water_surface = Utils.FindChild(karve.transform, "vfx_water_surface")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? karveFront_particles = Utils.FindChild(karve.transform, "front_particles")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? vikingShipAlt_particles = Utils.FindChild(vikingShip.transform, "alt_particles")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? karveTrail = Utils.FindChild(karve.transform, "Trail")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? karveRightSplash = Utils.FindChild(karve.transform, "RightSplash")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? karveLeftSplash = Utils.FindChild(karve.transform, "LeftSplash")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? karveRudder = Utils.FindChild(karve.transform, "rudder")?.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer? karveVfx_WaterImpact = null;
            ParticleSystemRenderer? karveVfx_Place = null;
            ParticleSystemRenderer? karveVfx_watersplash = null;
            for(int i = 0; i < karveShip.m_waterImpactEffect.m_effectPrefabs.Length; i++)
            {
                if(karveShip.m_waterImpactEffect.m_effectPrefabs[i].m_prefab.TryGetComponent(out ParticleSystemRenderer karveVfx_WaterImpact1))
                    karveVfx_WaterImpact = karveVfx_WaterImpact1;
            }
            for(int i = 0; i < karvePiece.m_placeEffect.m_effectPrefabs.Length; i++)
            {
                if(karvePiece.m_placeEffect.m_effectPrefabs[i].m_prefab.TryGetComponent(out ParticleSystemRenderer karveVfx_Place1))
                    karveVfx_Place = karveVfx_Place1;
            }
            for(int i = 0; i < karveWaterTrigger?.m_effects.m_effectPrefabs.Length; i++)
            {
                if(karveWaterTrigger.m_effects.m_effectPrefabs[i].m_prefab.TryGetComponent(out ParticleSystemRenderer vfx_watersplash1))
                    karveVfx_watersplash = vfx_watersplash1;
            }

            for(int i = 0; i < ships.Count; i++)
            {
                GameObject currentShip = ships[i];
                Ship currentShipShipCmp = currentShip.GetComponent<Ship>();
                Piece currentShipPiece = currentShip.GetComponent<Piece>();
                MeshRenderer? waternmask = Utils.FindChild(currentShip.transform, "watermask_waternmask")?.GetComponent<MeshRenderer>();
                WaterTrigger? waterTrigger = Utils.FindChild(currentShip.transform, "vfx_water_surface")?.GetComponent<WaterTrigger>();
                ParticleSystemRenderer? vfx_water_surface = Utils.FindChild(currentShip.transform, "vfx_water_surface")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? front_particles = Utils.FindChild(currentShip.transform, "front_particles")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? alt_particles = Utils.FindChild(currentShip.transform, "alt_particles")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? trail = Utils.FindChild(currentShip.transform, "Trail")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? rightSplash = Utils.FindChild(currentShip.transform, "RightSplash")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? leftSplash = Utils.FindChild(currentShip.transform, "LeftSplash")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? rudder = Utils.FindChild(currentShip.transform, "rudder")?.GetComponent<ParticleSystemRenderer>();
                ParticleSystemRenderer? vfx_WaterImpact = null;
                ParticleSystemRenderer? vfx_Place = null;
                ParticleSystemRenderer? vfx_watersplash = null;
                for(int ii = 0; ii < currentShipShipCmp.m_waterImpactEffect.m_effectPrefabs.Length; ii++)
                {
                    if(currentShipShipCmp.m_waterImpactEffect.m_effectPrefabs[ii].m_prefab.TryGetComponent(out ParticleSystemRenderer vfx_WaterImpact1))
                        vfx_WaterImpact = vfx_WaterImpact1;
                }
                for(int ii = 0; ii < currentShipPiece.m_placeEffect.m_effectPrefabs.Length; ii++)
                {
                    if(currentShipPiece.m_placeEffect.m_effectPrefabs[ii].m_prefab.TryGetComponent(out ParticleSystemRenderer vfx_Place1))
                        vfx_Place = vfx_Place1;
                }
                for(int ii = 0; ii < waterTrigger?.m_effects.m_effectPrefabs.Length; ii++)
                {
                    if(waterTrigger.m_effects.m_effectPrefabs[ii].m_prefab.TryGetComponent(out ParticleSystemRenderer vfx_watersplash1))
                        vfx_watersplash = vfx_watersplash1;
                }

#pragma warning disable CS8602
                if(waternmask)
                {
                    waternmask.material.shader = Shader.Find("Custom/WaterMask");
                    if(ShipFixingDebuging) Debug.Log($"Fixing waternmask in {currentShip.name}");
                }
                if(vfx_water_surface && karveVfx_water_surface)
                {
                    vfx_water_surface.materials = karveVfx_water_surface.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing vfx_water_surface in {currentShip.name}");
                }
                if(front_particles && karveFront_particles)
                {
                    front_particles.materials = karveFront_particles.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing front_particles in {currentShip.name}");
                }
                if(trail && karveTrail)
                {
                    trail.materials = karveTrail.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing trail in {currentShip.name}");
                }
                if(rightSplash && karveRightSplash)
                {
                    rightSplash.materials = karveRightSplash.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing rightSplash in {currentShip.name}");
                }
                if(leftSplash && karveLeftSplash)
                {
                    leftSplash.materials = karveLeftSplash.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing leftSplash in {currentShip.name}");
                }
                if(rudder && karveRudder)
                {
                    rudder.materials = karveRudder.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing rudder in {currentShip.name}");
                }
                if(vfx_WaterImpact && karveVfx_WaterImpact)
                {
                    vfx_WaterImpact.materials = karveVfx_WaterImpact.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing vfx_WaterImpact in {currentShip.name}");
                }
                if(vfx_Place && karveVfx_Place)
                {
                    vfx_Place.materials = karveVfx_Place.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing vfx_Place in {currentShip.name}");
                }
                if(alt_particles && vikingShipAlt_particles)
                {
                    alt_particles.materials = vikingShipAlt_particles.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing alt_particles in {currentShip.name}");
                }
                if(vfx_watersplash && karveVfx_watersplash)
                {
                    vfx_watersplash.materials = karveVfx_watersplash.materials;
                    if(ShipFixingDebuging) Debug.Log($"Fixing vfx_watersplash in {currentShip.name}");
                }
#pragma warning restore CS8602
            }
        }

        [HarmonyPriority(Priority.VeryHigh)]
        private static void SmokeFix(Smoke __instance)
        {
            if(FixSmoke) __instance.GetComponent<Renderer>().material.shader = Shader.Find("Custom/LitParticles");
        }

        public enum ShaderType
        {
            AlphaParticle,
            LitParticles,
            ParticleDecal,
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

