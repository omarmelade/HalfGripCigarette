using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cigarette
{
	/// <summary>
	/// The cigarette as the cockpit hand holds it (the game's own Grabbable): it follows the hand exactly, never turned (pixel art).
	/// Let go, it falls in the cockpit's plane; <see cref="Dash"/> puts it back on the dash. A right click while it is held is a drag:
	/// for a moment it goes to the mouth (the bottom middle of the view) instead of following the hand.
	/// </summary>
	internal sealed class CigGrab : Grabbable
	{
		internal enum Mode { Rest, Held, Drag, Falling }

		internal Mode State = Mode.Rest;
		internal Action RightClicked, Dropped;
		internal Vector2 Mouth;
		internal float DragUntil;
		internal float Gravity = 30f;

		protected override void OnGrab()
		{
			State = Mode.Held;
			velocity = Vector2.zero;
		}

		protected override void OnRelease()
		{
			if (State != Mode.Drag)
			{
				Fall();
			}
		}

		protected override void OnRightClick() => RightClicked?.Invoke();

		private void Fall()
		{
			State = Mode.Falling;
			velocity = Vector2.zero;
			Dropped?.Invoke();
		}

		protected override void StepPhysics(float dt) => StepDirect(dt);

		protected override void StepDirect(float dt)
		{
			if (State == Mode.Drag)
			{
				position = Vector2.Lerp(position, Mouth, 1f - Mathf.Exp(-12f * dt));
				if (Time.unscaledTime >= DragUntil)
				{
					if (IsGrabbed)
					{
						State = Mode.Held;
					}
					else
					{
						Fall();
					}
				}
			}
			else if (IsGrabbed)
			{
				position = constrainedPoint - grabbedPoint;
			}
			else if (State == Mode.Falling)
			{
				velocity += new Vector2(0f, -Gravity) * dt;
				position += velocity * dt;
			}
			angle = 0f;
		}
	}

	/// <summary>
	/// The kit in one cockpit: the lighter right of the steering wheel and the cigarette lying on top of the dash, both placed from the
	/// wheel's own sprite. Flat pixel sprites in the wheel's plane, like the rest of the cockpit: the lit ones in a copy of the wheel's
	/// material (the cockpit's lit shader reads the material's texture, not the sprite's), the glowing ones (ember, hot lighter, smoke)
	/// unlit. Grab targets sit at the lowest hover priority so they never take the hand from a real control.
	/// </summary>
	internal sealed class Dash : MonoBehaviour
	{
		internal static Dash Active;

		// shortcut: the cockpit's pixel is taken as 0.05 units (what the Stereo mod measures in 0.419); measure the sprites if a car differs
		private const float Texel = 0.05f;
		private const int Len = 14, BurnPerDrag = 3;
		private const int HoverPriority = -100;
		private const float DragTime = 0.7f, FallTime = 1.2f, LightReach = 0.35f;

		private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
		private Transform interior;
		private int layer, sortingLayer, sortingOrder;
		private float toward = -1f;
		private Material litBase, unlit;
		private Sprite pixel;
		private Camera cam;

		private CigGrab cig;
		private Texture2D cigTexture;
		private SpriteRenderer ember, glow;
		private readonly SpriteRenderer[] smoke = new SpriteRenderer[5];
		private Vector3 rest;
		private Vector2 lighterAt;
		private bool lit;
		private int taken;
		private float heat, fellAt;

		private void Awake() => Active = this;

		private void Start()
		{
			try
			{
				Build();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not build the cigarette kit: " + e);
			}
		}

		private void Build()
		{
			interior = transform.parent;
			layer = gameObject.layer = interior.gameObject.layer;
			Grabber hand = FindFirstObjectByType<Grabber>();
			cam = hand != null ? hand.viewCamera : null;
			if (cam != null)
			{
				toward = interior.InverseTransformPoint(cam.transform.position).z > 0f ? 1f : -1f;
			}

			// The wheel's biggest sprite: where it is, and whose material and sorting we borrow.
			SpriteRenderer wheel = null;
			float area = 0f;
			NSteeringWheel w = interior.GetComponentInChildren<NSteeringWheel>(true);
			foreach (SpriteRenderer r in w != null ? w.GetComponentsInChildren<SpriteRenderer>(true) : new SpriteRenderer[0])
			{
				if (r.sprite == null)
				{
					continue;
				}
				float a = r.sprite.bounds.size.x * r.sprite.bounds.size.y * Mathf.Abs(r.transform.lossyScale.x * r.transform.lossyScale.y);
				if (a > area)
				{
					area = a;
					wheel = r;
				}
			}
			Vector3 centre = new Vector3(-1.8f, -0.36f, 1.14f); // the game's wheel in 0.419, if none is found
			float radius = 2.2f;
			if (wheel != null)
			{
				centre = interior.InverseTransformPoint(wheel.transform.TransformPoint(wheel.sprite.bounds.center));
				Vector3 ext = wheel.sprite.bounds.extents;
				radius = Mathf.Max(ext.x, ext.y) * Mathf.Abs(wheel.transform.lossyScale.x) / Mathf.Max(1e-6f, Mathf.Abs(interior.lossyScale.x));
				sortingLayer = wheel.sortingLayerID;
				sortingOrder = wheel.sortingOrder;
				litBase = wheel.sharedMaterial;
			}
			foreach (SpriteRenderer r in interior.GetComponentsInChildren<SpriteRenderer>(true))
			{
				Material m = r.sharedMaterial;
				if (m != null && m.shader != null && m.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					unlit = m;
					break;
				}
			}
			if (unlit == null)
			{
				Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
				if (s != null)
				{
					unlit = Own(new Material(s));
				}
			}
			Texture2D white = Texture(1, 1);
			white.SetPixel(0, 0, Color.white);
			white.Apply();
			pixel = Own(Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f / Texel));

			// The lighter: on the dash between the wheel's rim and the shifter.
			lighterAt = new Vector2(centre.x + radius + 1f + Mod.LighterX.Value, centre.y - 0.3f + Mod.LighterY.Value);
			GameObject lighter = Part("Lighter", transform, new Vector3(lighterAt.x, lighterAt.y, centre.z + toward * 0.02f));
			Texture2D lt = Texture(7, 7);
			for (int y = 0; y < 7; y++)
			{
				for (int x = 0; x < 7; x++)
				{
					float d = Vector2.Distance(new Vector2(x, y), new Vector2(3f, 3f));
					lt.SetPixel(x, y, d > 3.4f ? Color.clear : d > 2.4f ? new Color(0.55f, 0.55f, 0.58f) : d > 1.4f ? new Color(0.12f, 0.12f, 0.13f) : new Color(0.3f, 0.16f, 0.1f));
				}
			}
			lt.Apply();
			Picture(lighter, "Knob", lt, Vector3.zero, 1);
			glow = Dot(lighter, "Glow", new Vector3(0f, 0f, toward * 0.01f), 2);
			glow.transform.localScale = new Vector3(3f, 3f, 1f);

			// The cigarette: lying on top of the dash, above the wheel's right half. Its local position is set before the Grabbable is
			// added: Grabbable.Awake reads its pose from the transform.
			rest = new Vector3(centre.x + 1.9f + Mod.RestX.Value, centre.y + 2.3f + Mod.RestY.Value, centre.z + toward * 0.05f);
			GameObject go = Part("Cigarette", transform, rest);
			cigTexture = Texture(Len, 2);
			Picture(go, "Paper", cigTexture, Vector3.zero, 3);
			ember = Dot(go, "Ember", Vector3.zero, 4);
			ember.transform.localScale = new Vector3(1f, 2f, 1f);
			for (int i = 0; i < smoke.Length; i++)
			{
				smoke[i] = Dot(go, "Smoke" + i, Vector3.zero, 4);
			}
			GameObject touch = Part("Touch", go.transform, new Vector3(0f, 0f, toward * 0.05f));
			InteriorSquareCollider target = touch.AddComponent<InteriorSquareCollider>();
			target.width = Len * Texel;
			target.height = 2 * Texel;
			target.hitBoxExpand = 0.15f; // it is two pixels thin: easy to miss otherwise
			target.localNormalPlane = Vector3.forward;
			target.sortingLayer = HoverPriority;
			cig = go.AddComponent<CigGrab>();
			cig.attachedCollider = target;
			cig.keepPhysicalCursorClose = false;
			cig.RightClicked = Drag;
			cig.Dropped = () => fellAt = Time.unscaledTime;
			Draw();
			Mod.Log.LogInfo("Cigarette kit in '" + interior.name + "': wheel at " + centre.ToString("0.00") + " radius " + radius.ToString("0.00") + ", lighter at " + lighterAt.ToString("0.00") + ", cigarette at " + rest.ToString("0.00") + ", lit material '" + (litBase != null ? litBase.shader.name : "none") + "', unlit '" + (unlit != null ? unlit.shader.name : "none") + "', camera '" + (cam != null ? cam.name : "none") + "'");
		}

		/// <summary>Pixels burnt off the tip (the left end).</summary>
		private int Burnt => taken * BurnPerDrag;

		/// <summary>The tip, from the cigarette's middle.</summary>
		private Vector2 Tip => new Vector2((-Len * 0.5f + Burnt + 0.5f) * Texel, 0f);

		private void Draw()
		{
			for (int x = 0; x < Len; x++)
			{
				Color c = x < Burnt ? Color.clear : x >= Len - 4 ? new Color(0.82f, 0.55f, 0.25f) : x == Burnt && taken > 0 ? new Color(0.35f, 0.35f, 0.35f) : new Color(0.93f, 0.93f, 0.88f);
				cigTexture.SetPixel(x, 1, c);
				cigTexture.SetPixel(x, 0, c == Color.clear ? c : c * new Color(0.8f, 0.8f, 0.8f, 1f));
			}
			cigTexture.Apply();
			ember.transform.localPosition = new Vector3(Tip.x - Texel, 0f, toward * 0.01f);
		}

		/// <summary>A right click with the cigarette in the hand.</summary>
		private void Drag()
		{
			if (!lit || cig.State != CigGrab.Mode.Held || taken >= Mod.Drags.Value)
			{
				return;
			}
			taken++;
			Draw();
			cig.Mouth = Mouth();
			cig.DragUntil = Time.unscaledTime + DragTime;
			cig.State = CigGrab.Mode.Drag;
			Smoker.Drag();
		}

		/// <summary>The bottom middle of the view, in the cigarette's plane.</summary>
		private Vector2 Mouth()
		{
			if (cam != null)
			{
				Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.06f, 0f));
				Vector3 o = interior.InverseTransformPoint(ray.origin), dir = interior.InverseTransformDirection(ray.direction);
				float k = Mathf.Abs(dir.z) > 1e-4f ? (rest.z - o.z) / dir.z : -1f;
				if (k > 0f)
				{
					return o + dir * k;
				}
			}
			return new Vector2(rest.x, rest.y - 4f);
		}

		private void Update()
		{
			if (cig == null || InteriorMath.Paused)
			{
				return;
			}
			float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;

			// Lighting: the unlit tip held on the lighter, which heats up while it is there.
			bool onLighter = !lit && cig.State == CigGrab.Mode.Held && taken < Mod.Drags.Value && Vector2.Distance(cig.position + Tip, lighterAt) < LightReach;
			heat = onLighter ? heat + dt : 0f;
			glow.enabled = onLighter;
			glow.color = Color.Lerp(new Color(0.5f, 0.1f, 0f), new Color(1f, 0.55f, 0.1f), heat / Mathf.Max(0.01f, Mod.LightTime.Value));
			if (onLighter && heat >= Mod.LightTime.Value)
			{
				lit = true;
			}

			// Dropped: it falls, then a cigarette is back on the dash (a fresh one if this one was finished).
			if (cig.State == CigGrab.Mode.Falling && now - fellAt > FallTime)
			{
				if (taken >= Mod.Drags.Value)
				{
					taken = 0;
					lit = false;
					Draw();
				}
				cig.State = CigGrab.Mode.Rest;
				cig.ResetRestPose(rest, Quaternion.identity);
			}

			ember.enabled = lit;
			ember.color = new Color(1f, 0.3f + 0.2f * Mathf.Sin(now * 9f), 0.05f);
			for (int i = 0; i < smoke.Length; i++)
			{
				float p = Mathf.Repeat(now * 0.4f + i / (float)smoke.Length, 1f);
				smoke[i].enabled = lit;
				smoke[i].transform.localPosition = new Vector3(Tip.x - Texel + Mathf.Sin(p * 6f + i) * 0.08f, 0.1f + p * 1.2f, toward * 0.01f);
				smoke[i].transform.localScale = Vector3.one * (1f + 2f * p);
				smoke[i].color = new Color(0.8f, 0.8f, 0.8f, (1f - p) * 0.4f);
			}
		}

		private void OnDestroy()
		{
			if (Active == this)
			{
				Active = null;
			}
			foreach (UnityEngine.Object o in owned)
			{
				if (o != null)
				{
					Destroy(o);
				}
			}
		}

		// ------------------------------------------------------------------ building blocks

		private T Own<T>(T thing) where T : UnityEngine.Object
		{
			thing.hideFlags = HideFlags.HideAndDontSave;
			owned.Add(thing);
			return thing;
		}

		private Texture2D Texture(int w, int h) => Own(new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });

		private GameObject Part(string name, Transform parent, Vector3 local)
		{
			GameObject go = new GameObject(name);
			go.layer = layer;
			go.transform.SetParent(parent, false);
			go.transform.localPosition = local;
			return go;
		}

		private SpriteRenderer Renderer(GameObject on, string name, Sprite sprite, Vector3 local, Material material, int above)
		{
			SpriteRenderer r = Part(name, on.transform, local).AddComponent<SpriteRenderer>();
			r.sprite = sprite;
			if (material != null)
			{
				r.sharedMaterial = material;
			}
			r.sortingLayerID = sortingLayer;
			r.sortingOrder = sortingOrder + above;
			return r;
		}

		/// <summary>A lit sprite of the whole texture, centred on <paramref name="local"/>.</summary>
		private void Picture(GameObject on, string name, Texture2D tex, Vector3 local, int above)
		{
			Material material = unlit;
			if (litBase != null)
			{
				material = Own(new Material(litBase));
				foreach (string p in new[] { "_BaseMap", "_MainTex" })
				{
					if (material.HasProperty(p))
					{
						material.SetTexture(p, tex);
						material.SetTextureScale(p, Vector2.one);
						material.SetTextureOffset(p, Vector2.zero);
					}
				}
				foreach (string p in new[] { "_BaseColor", "_Color" })
				{
					if (material.HasProperty(p))
					{
						material.SetColor(p, Color.white);
					}
				}
			}
			Sprite sprite = Own(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f / Texel, 0, SpriteMeshType.FullRect));
			Renderer(on, name, sprite, local, material, above);
		}

		/// <summary>One unlit cockpit pixel, coloured and scaled by its user: it glows.</summary>
		private SpriteRenderer Dot(GameObject on, string name, Vector3 local, int above)
		{
			SpriteRenderer r = Renderer(on, name, pixel, local, unlit, above);
			r.enabled = false;
			return r;
		}
	}
}
