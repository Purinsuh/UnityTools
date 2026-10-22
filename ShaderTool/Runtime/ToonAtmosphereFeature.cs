using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Dessine l'atmosphere (ToonAtmosphere) apres les objets opaques et le ciel, avant les transparents.
// Sans Volume actif (intensite 0), la passe n'est pas ajoutee : aucun cout.
public class ToonAtmosphereFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader shader;
    private Material material;
    private AtmospherePass pass;

    public override void Create()
    {
#if UNITY_EDITOR
        // Effet ajouté à la main ("Add Renderer Feature") : le shader est retrouvé ET enregistré dans le renderer,
        // sinon la référence n'existe qu'en mémoire et le jeu compilé n'aurait pas d'atmosphère.
        if (shader == null)
        {
            shader = Shader.Find("Hidden/Toon/Atmosphere");
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
        pass = new AtmospherePass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection || shader == null) return;
        // Caméra superposée (interface, arme...) : l'image de la caméra de base a déjà son atmosphère, et la
        // profondeur de la caméra superposée ne contient que ses propres objets (tout le reste serait pris pour du ciel)
        if (renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
        var settings = VolumeManager.instance.stack.GetComponent<ToonAtmosphere>();
        if (settings == null || !settings.IsActive()) return;

        if (material == null) material = CoreUtils.CreateEngineMaterial(shader);
        pass.Setup(material, settings);
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        // Demande la profondeur au niveau de la caméra aussi : avec un renderer en "Force Prepass", la seule demande
        // de la passe ne suffit pas quand la texture de profondeur est désactivée dans l'asset URP (elle resterait
        // vide et toute l'image serait prise pour du ciel).
        renderingData.cameraData.requiresDepthTexture = true;
        pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    private class AtmospherePass : ScriptableRenderPass
    {
        private static readonly int NearColorId = Shader.PropertyToID("_AtmoNearColor");
        private static readonly int FarColorId = Shader.PropertyToID("_AtmoFarColor");
        private static readonly int SunColorId = Shader.PropertyToID("_AtmoSunColor");
        private static readonly int ParamsId = Shader.PropertyToID("_AtmoParams");
        private static readonly int Params2Id = Shader.PropertyToID("_AtmoParams2");
        private static readonly int NightBlendId = Shader.PropertyToID("_ToonNightBlend");

        private Material material;

        private class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public void Setup(Material mat, ToonAtmosphere s)
        {
            material = mat;
            // La nuit (ToonDayNight), le voile prend la teinte de nuit au lieu de rester clair
            Color night = Color.Lerp(Color.white, s.nightTint.value, Shader.GetGlobalFloat(NightBlendId));
            material.SetColor(NearColorId, s.nearColor.value * night);
            material.SetColor(FarColorId, s.farColor.value * night);
            material.SetColor(SunColorId, s.sunColor.value * night);
            material.SetVector(ParamsId, new Vector4(s.intensity.value, s.startDistance.value, s.density.value, s.colorDistance.value));
            material.SetVector(Params2Id, new Vector4(s.heightBase.value, s.heightFalloff.value, s.skyAmount.value, s.sunGlow.value));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            // Copie de l'image (on ne peut pas lire et ecrire la meme texture dans une passe)
            var desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            desc.name = "_ToonAtmosphereSource";
            desc.clearBuffer = false;
            var copy = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(resources.activeColorTexture, copy, Vector2.one, Vector2.zero, passName: "Toon Atmosphere (copie)");

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Toon Atmosphere", out var data))
            {
                data.source = copy;
                data.material = material;
                builder.UseTexture(copy, AccessFlags.Read);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
            }
        }
    }
}
