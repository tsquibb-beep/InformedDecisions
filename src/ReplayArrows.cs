using Godot;

namespace InformedDecisions;

/// <summary>
/// The replay symbol: two arching arrows around the number in the replay gem, one over the top
/// pointing clockwise and one under the bottom (the usual "repeat" icon), in the cost text's cream
/// with its outline. Drawn by a shader on a ColorRect the size of the gem, a child of it placed before
/// the number so the number draws on top. A signed-distance shape, so it stays crisp at any card size.
/// </summary>
internal static class ReplayArrows
{
    private const string ShaderCode = @"
shader_type canvas_item;

// Lengths are in units of the rect's half-width.
uniform float radius = 0.62;
uniform float thickness = 0.12;
uniform float span = 62.0; // degrees either side of the top that each arc covers
uniform float head_width = 0.36;
uniform float head_length = 0.24;
uniform float outline = 0.07;
uniform vec4 fill : source_color = vec4(1.0, 0.96, 0.86, 1.0);
uniform vec4 edge : source_color = vec4(0.0, 0.0, 0.0, 1.0);

float sd_triangle(vec2 p, vec2 p0, vec2 p1, vec2 p2) {
    vec2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
    vec2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
    vec2 pq0 = v0 - e0 * clamp(dot(v0, e0) / dot(e0, e0), 0.0, 1.0);
    vec2 pq1 = v1 - e1 * clamp(dot(v1, e1) / dot(e1, e1), 0.0, 1.0);
    vec2 pq2 = v2 - e2 * clamp(dot(v2, e2) / dot(e2, e2), 0.0, 1.0);
    float s = sign(e0.x * e2.y - e0.y * e2.x);
    vec2 d = min(min(vec2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                     vec2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                     vec2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
    return -sqrt(d.x) * sign(d.y);
}

// One arrow: an arc over the top with its head on the right end, pointing clockwise.
float sd_arrow(vec2 p) {
    float h = radians(span);
    float a = atan(p.x, p.y); // 0 at the top, positive to the right
    float arc;
    if (abs(a) <= h) {
        arc = abs(length(p) - radius) - thickness * 0.5;
    } else {
        vec2 l = radius * vec2(-sin(h), cos(h));
        vec2 r = radius * vec2(sin(h), cos(h));
        arc = min(length(p - l), length(p - r)) - thickness * 0.5;
    }
    vec2 n = vec2(sin(h), cos(h));  // outward at the right end
    vec2 t = vec2(cos(h), -sin(h)); // clockwise along the arc
    vec2 e = radius * n;
    float head = sd_triangle(p, e + n * head_width * 0.5, e - n * head_width * 0.5, e + t * head_length);
    return min(arc, head);
}

void fragment() {
    vec2 p = (UV - 0.5) * vec2(2.0, -2.0);
    // The second arrow is the first turned half a circle.
    float d = min(sd_arrow(p), sd_arrow(-p));
    float aa = fwidth(d);
    float inner = 1.0 - smoothstep(-aa, aa, d);
    float outer = 1.0 - smoothstep(-aa, aa, d - outline);
    COLOR = vec4(mix(edge.rgb, fill.rgb, inner), outer * edge.a);
}
";

    private static Shader? _shader;

    /// <summary>Shows or hides the arrows on <paramref name="gem"/>, creating them the first time.</summary>
    public static void Update(ref ColorRect? arrows, TextureRect gem, Label number, bool show, Color fill, Color outline)
    {
        if (!show)
        {
            if (arrows != null && GodotObject.IsInstanceValid(arrows)) arrows.Visible = false;
            return;
        }

        if (arrows == null || !GodotObject.IsInstanceValid(arrows))
        {
            arrows = new ColorRect
            {
                Name = "InformedDecisionsReplayArrows",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = ShaderCode } },
            };
            gem.AddChild(arrows);
            // Under the number.
            gem.MoveChild(arrows, number.GetIndex());
        }

        var material = (ShaderMaterial)arrows.Material;
        material.SetShaderParameter("fill", fill);
        material.SetShaderParameter("edge", outline);
        arrows.Position = Vector2.Zero;
        arrows.Size = gem.Size;
        arrows.Visible = true;
    }
}
