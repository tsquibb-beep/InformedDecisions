using System;
using Godot;

namespace InformedDecisions;

/// <summary>
/// Optional motion on the replay gem, so a card that will be played again catches the eye:
///
/// * **ripple:** every few seconds a copy of the gem grows out from behind it and fades, like an
///   echo of the card being played again.
/// * **glow:** a soft halo behind the gem that slowly brightens and dims.
///
/// Each is one TextureRect, a child of the replay gem drawn behind it (<c>ShowBehindParent</c>), so
/// it moves, fades and hides with the gem. Both are animated by the shader's TIME, which every card
/// shares, so all animated gems in the hand pulse together and no per-frame code is needed. The
/// node uses the gem's own texture, so the effect takes the character's gem colour by default.
///
/// The gem texture comes from the game's UI atlas, so the shader gets the atlas region and maps the
/// rect's UV back to the gem's own 0..1 square itself.
/// </summary>
internal static class ReplayEffects
{
    public const string Ripple = "ripple";
    public const string Glow = "glow";
    public const string Compare = "compare";
    public const string None = "none";

    // How much bigger than the gem each effect's rect is, so the effect has room outside the gem.
    private const float RippleExpand = 1.9f;
    private const float GlowExpand = 1.8f;

    private const string Common = @"
shader_type canvas_item;

uniform vec4 region = vec4(0.0, 0.0, 1.0, 1.0);
uniform float expand = 1.8;
uniform float period = 2.4;
uniform float strength = 0.8;
uniform vec4 tint : source_color = vec4(1.0);
uniform float use_tint = 0.0;

// The gem texture at a point in the gem's own 0..1 square, premultiplied so blurred taps average
// properly; nothing outside the square, so the taps never pick up neighbours in the atlas.
vec4 gem(sampler2D tex, vec2 g) {
    if (g.x < 0.01 || g.y < 0.01 || g.x > 0.99 || g.y > 0.99) {
        return vec4(0.0);
    }
    vec4 t = texture(tex, region.xy + g * region.zw);
    return vec4(t.rgb * t.a, t.a);
}

vec2 gem_uv(vec2 uv) {
    return ((uv - region.xy) / region.zw - 0.5) * expand + 0.5;
}

vec3 colour_of(vec4 c, float boost) {
    vec3 own = min(c.rgb / max(c.a, 0.001) * boost, vec3(1.0));
    return mix(own, tint.rgb, use_tint);
}
";

    private const string RippleCode = Common + @"
render_mode blend_mix;

uniform float grow = 0.75;   // the copy ends this much bigger than the gem
uniform float active = 0.55; // share of each period the ripple runs; the rest is a pause

void fragment() {
    float p = fract(TIME / max(period, 0.1)) / max(active, 0.05);
    if (p > 1.0) {
        COLOR = vec4(0.0);
    } else {
        // Fast out, slow settle, fading as it grows.
        float e = 1.0 - (1.0 - p) * (1.0 - p);
        vec4 c = gem(TEXTURE, (gem_uv(UV) - 0.5) / (1.0 + grow * e) + 0.5);
        float fade = (1.0 - p) * (1.0 - p);
        COLOR = vec4(colour_of(c, 1.5), c.a * fade * strength);
    }
}
";

    private const string GlowCode = Common + @"
render_mode blend_add;

uniform float radius = 0.1; // blur, as a share of the gem's width
uniform float low = 0.25;   // dimmest point of the pulse, as a share of full strength

void fragment() {
    vec2 g = gem_uv(UV);
    vec4 acc = gem(TEXTURE, g) * 1.5;
    float total = 1.5;
    for (int i = 0; i < 12; i++) {
        float a = float(i) * 0.5235988;
        vec2 d = vec2(cos(a), sin(a)) * radius;
        acc += gem(TEXTURE, g + d);
        acc += gem(TEXTURE, g + d * 2.0) * 0.6;
        total += 1.6;
    }
    acc /= total;
    float pulse = mix(low, 1.0, 0.5 + 0.5 * sin(TIME * 6.2831853 / max(period, 0.1)));
    COLOR = vec4(colour_of(acc, 1.8), acc.a * strength * pulse);
}
";

    private static Shader? _rippleShader;
    private static Shader? _glowShader;

    /// <summary>Which effect a card gets. "compare" alternates them along the hand.</summary>
    public static string Resolve(string? setting, int slot) =>
        (setting ?? None).Trim().ToLowerInvariant() switch
        {
            Ripple => Ripple,
            Glow => Glow,
            Compare => slot % 2 == 0 ? Ripple : Glow,
            _ => None,
        };

    /// <summary>
    /// Shows <paramref name="kind"/> behind <paramref name="gem"/>, building or replacing the node as
    /// needed; <see cref="None"/> hides it.
    /// </summary>
    public static void Update(ref TextureRect? effect, ref string? effectKind, TextureRect gem, string kind,
        InfoConfig config)
    {
        if (kind == None)
        {
            if (effect != null && GodotObject.IsInstanceValid(effect)) effect.Visible = false;
            return;
        }

        if (effect == null || !GodotObject.IsInstanceValid(effect) || effectKind != kind)
        {
            if (effect != null && GodotObject.IsInstanceValid(effect)) effect.QueueFree();
            effect = Create(kind, config);
            effectKind = kind;
            gem.AddChild(effect);
        }

        float expand = kind == Ripple ? RippleExpand : GlowExpand;
        Vector2 size = gem.Size * expand;
        effect.Size = size;
        effect.Position = (gem.Size - size) / 2;
        if (effect.Texture != gem.Texture)
        {
            effect.Texture = gem.Texture;
            ((ShaderMaterial)effect.Material).SetShaderParameter("region", Region(gem.Texture));
        }

        effect.Visible = true;
    }

    private static TextureRect Create(string kind, InfoConfig config)
    {
        bool ripple = kind == Ripple;
        var material = new ShaderMaterial
        {
            Shader = ripple
                ? _rippleShader ??= new Shader { Code = RippleCode }
                : _glowShader ??= new Shader { Code = GlowCode },
        };
        material.SetShaderParameter("expand", ripple ? RippleExpand : GlowExpand);
        material.SetShaderParameter("period", Math.Max(0.1f, ripple ? config.ReplayRipplePeriod : config.ReplayGlowPeriod));
        material.SetShaderParameter("strength", Math.Clamp(ripple ? config.ReplayRippleStrength : config.ReplayGlowStrength, 0f, 2f));
        if (config.ReplayEffectColor is { Length: >= 3 } c)
        {
            material.SetShaderParameter("tint", new Color(c[0], c[1], c[2]));
            material.SetShaderParameter("use_tint", 1f);
        }

        return new TextureRect
        {
            Name = ripple ? "InformedDecisionsRipple" : "InformedDecisionsGlow",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ShowBehindParent = true,
            Material = material,
            Visible = false,
        };
    }

    /// <summary>Where the gem sits in its texture, as UV: the atlas region, or the whole texture.</summary>
    private static Vector4 Region(Texture2D? texture)
    {
        if (texture is AtlasTexture { Atlas: { } atlas } at && atlas.GetWidth() > 0 && atlas.GetHeight() > 0)
        {
            Vector2 page = atlas.GetSize();
            Rect2 r = at.Region;
            return new Vector4(r.Position.X / page.X, r.Position.Y / page.Y, r.Size.X / page.X, r.Size.Y / page.Y);
        }

        return new Vector4(0, 0, 1, 1);
    }
}
