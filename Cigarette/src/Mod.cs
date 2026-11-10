using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;
using HarmonyLib;
using UnityEngine;

namespace Cigarette
{
	/// <summary>
	/// A cigarette on the dash and a cigarette lighter beside the wheel, worked with the cockpit hand: grab the cigarette, hold its tip
	/// on the lighter until it glows, right click with it in the hand to take a drag. A drag slows the game and narrows the view
	/// for a few seconds. See Dash.cs for the cockpit side; this file is the settings and the effect.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;
		internal static ConfigEntry<float> Duration, Strength, Zoom, Vignette, LightTime, LighterX, LighterY, RestX, RestY;
		internal static ConfigEntry<int> Drags;

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Duration = ctx.Config.Bind("Drag", "Duration", 5f, "Seconds of slow motion and focus per drag.");
			Strength = ctx.Config.Bind("Drag", "TimeScale", 0.5f, new ConfigDescription("Game speed during the effect (1 = normal).", new AcceptableValueRange<float>(0.1f, 1f)));
			Drags = ctx.Config.Bind("Drag", "PerCigarette", 3, new ConfigDescription("Drags in one cigarette. Drop the butt to get a new one.", new AcceptableValueRange<int>(1, 4)));
			LightTime = ctx.Config.Bind("Lighter", "Seconds", 0.6f, "How long the tip must stay on the lighter.");
			Zoom = ctx.Config.Bind("Focus", "Zoom", 0.88f, new ConfigDescription("Field of view during the effect, as a share of normal (1 = no zoom).", new AcceptableValueRange<float>(0.5f, 1f)));
			Vignette = ctx.Config.Bind("Focus", "Vignette", 0.8f, new ConfigDescription("How dark the screen edges get (0 = off).", new AcceptableValueRange<float>(0f, 1f)));
			LighterX = ctx.Config.Bind("Place", "LighterX", 0f, "Moves the lighter sideways (cockpit units, right is positive; a cockpit pixel is 0.05). Applies from the next race.");
			LighterY = ctx.Config.Bind("Place", "LighterY", 0f, "Moves the lighter up or down.");
			RestX = ctx.Config.Bind("Place", "CigaretteX", 0f, "Moves the cigarette's place on the dash sideways.");
			RestY = ctx.Config.Bind("Place", "CigaretteY", 0f, "Moves the cigarette's place on the dash up or down.");

			var go = new GameObject("CigaretteMod");
			Object.DontDestroyOnLoad(go);
			go.AddComponent<Smoker>();
			ctx.OnUnload(() =>
			{
				Object.Destroy(go);
				if (Dash.Active != null)
				{
					Object.Destroy(Dash.Active.gameObject);
				}
			});
			ctx.PatchAll();
		}
	}

	/// <summary>Puts the kit in each cockpit (as the Stereo mod does: the steering wheel's parent is the cockpit) and runs the effect.</summary>
	internal class Smoker : MonoBehaviour
	{
		/// <summary>0 = normal, 1 = full effect. Read by the camera patch.</summary>
		internal static float Power;

		private static float left, since;
		private float nextLook;
		private bool applied, warned;
		private Texture2D vignette;

		internal static void Drag()
		{
			left = Mod.Duration.Value;
			since = 0f;
		}

		private void Update()
		{
			if (Dash.Active == null && Time.unscaledTime >= nextLook)
			{
				nextLook = Time.unscaledTime + 0.5f;
				try
				{
					NSteeringWheel wheel = FindFirstObjectByType<NSteeringWheel>();
					if (wheel != null && wheel.transform.parent != null)
					{
						var kit = new GameObject("CigaretteKit");
						kit.transform.SetParent(wheel.transform.parent, false);
						kit.AddComponent<Dash>();
					}
				}
				catch (System.Exception e)
				{
					if (!warned)
					{
						warned = true;
						Mod.Log.LogWarning("Could not put the cigarette in the cockpit: " + e);
					}
				}
			}
			if (Dash.Active == null)
			{
				left = 0f; // left the race
			}
			else if (Time.timeScale > 0f) // not while paused
			{
				left -= Time.unscaledDeltaTime;
				since += Time.unscaledDeltaTime;
			}
			Power = Mathf.MoveTowards(Power, left > 0f ? 1f : 0f, Time.unscaledDeltaTime * 2f);
			if (Power > 0f)
			{
				applied = true;
				TimeScale.Set(this, Mathf.Lerp(1f, Mod.Strength.Value, Power));
			}
			else if (applied)
			{
				applied = false;
				TimeScale.Clear(this);
			}
		}

		private void OnDestroy()
		{
			Power = 0f;
			left = 0f;
			if (applied)
			{
				TimeScale.Clear(this);
			}
		}

		private void OnGUI()
		{
			if (Power <= 0f || Event.current.type != EventType.Repaint)
			{
				return;
			}
			if (vignette == null)
			{
				vignette = MakeVignette();
			}
			GUI.color = new Color(0f, 0f, 0f, Power * Mod.Vignette.Value);
			GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette);
			// the breath out: smoke rising from the bottom of the view for the first seconds
			float u = Screen.height / 180f;
			for (int i = 0; i < 10 && since < 3f; i++)
			{
				float p = since / 3f + i * 0.03f;
				float s = (10f + 30f * p) * u;
				float x = Screen.width * 0.5f + (Mathf.Sin(i * 2.4f) * 30f + Mathf.Sin(p * 5f + i) * 8f) * u - s * 0.5f;
				GUI.color = new Color(0.8f, 0.8f, 0.8f, Mathf.Clamp01(1f - p) * 0.22f * Power);
				GUI.DrawTexture(new Rect(x, Screen.height - p * 110f * u - s, s, s), Texture2D.whiteTexture);
			}
			GUI.color = Color.white;
		}

		private static Texture2D MakeVignette()
		{
			const int n = 64;
			var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					float d = Vector2.Distance(new Vector2(x, y), new Vector2(n - 1, n - 1) / 2f) / (n / 2f);
					t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (d - 0.45f) / 0.75f)));
				}
			}
			t.Apply();
			return t;
		}
	}

	// The car eases the field of view toward its own target every frame, so give it its own value back before it runs and zoom after.
	[HarmonyPatch(typeof(CarControllerNew), "UpdateCameraEffects")]
	internal static class FocusZoom
	{
		private static float gameFov;
		private static Camera zoomed;

		private static void Prefix(Camera ___fpvCamera, bool ___isPlayer)
		{
			if (___isPlayer && ___fpvCamera != null && zoomed == ___fpvCamera)
			{
				___fpvCamera.fieldOfView = gameFov;
			}
		}

		private static void Postfix(Camera ___fpvCamera, bool ___isPlayer)
		{
			if (!___isPlayer || ___fpvCamera == null)
			{
				return;
			}
			zoomed = ___fpvCamera;
			gameFov = ___fpvCamera.fieldOfView;
			___fpvCamera.fieldOfView = gameFov * Mathf.Lerp(1f, Mod.Zoom.Value, Smoker.Power);
		}
	}
}
