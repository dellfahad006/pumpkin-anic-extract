using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// "Look Lab". Press N to open / close it (M = performance menu, L = cheat menu).
//
// Goal: find out, in ONE playtest, why the skinned characters (player hood, deer, monsters, chains) look different
// from normal sprites, and why the scared face can disappear. Every switch below changes one possible difference
// between a real sprite and the software-skinned copy, live, with no rebuild.
//
// How to use it: bring a deer / monster on screen (cheat menu L), flip a switch, look. After each experiment press
// "LOOKS RIGHT" or "STILL WRONG" - that writes the current switch state into the log. When done: "Copy log + report"
// and paste it to Claude. You do not have to describe anything in words.
public class FixLab : MonoBehaviour
{
	private const bool Enabled = true; // set to false to remove the lab from a build

	private static FixLab instance;

	private static readonly string[] PartLabels = { "All", "Player", "Deer", "Deer body", "Ghoul", "Wendigo", "Chains" };
	private static readonly string[] PartKeys = { "", "Player/", "Deer/", "deerBodyAnimator", "GhoulEnemy", "Wendigo/wendigo", "ChainL" };

	private bool visible;
	private FixLabGui gui;
	private string report = "";
	private float reportTimer;
	private Transform faces;
	private GUIStyle small;
	private Vector2 scroll;
	private Vector2 logScroll;
	private readonly StringBuilder log = new StringBuilder();
	private string logText = "";
	private bool brightDirty;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		if (!Enabled || instance != null)
		{
			return;
		}
		GameObject go = new GameObject("FixLab");
		DontDestroyOnLoad(go);
		instance = go.AddComponent<FixLab>();
	}

	private void Awake()
	{
		gui = gameObject.AddComponent<FixLabGui>();
		gui.owner = this;
		gui.enabled = false;
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.N))
		{
			visible = !visible;
			gui.enabled = visible;
			reportTimer = 99f;
			if (visible && log.Length == 0)
			{
				Note("Look Lab opened. Unity " + Application.unityVersion + ", " + SystemInfo.graphicsDeviceType + ", scene " + SceneManager.GetActiveScene().name);
			}
		}
		if (visible)
		{
			reportTimer += Time.unscaledDeltaTime;
			if (reportTimer >= 0.7f)
			{
				reportTimer = 0f;
				report = BuildReport();
				faces = FindFaces();
			}
		}
	}

	// ---------------------------------------------------------------------------------------------
	// log
	// ---------------------------------------------------------------------------------------------
	private void Note(string text)
	{
		int t = (int)Time.realtimeSinceStartup;
		log.Append('[').Append(t / 60).Append(':').Append((t % 60).ToString("00")).Append("] ").Append(text).Append('\n');
		if (log.Length > 14000)
		{
			log.Remove(0, log.Length - 10000);
		}
		logText = log.ToString();
	}

	// ---------------------------------------------------------------------------------------------
	// reports
	// ---------------------------------------------------------------------------------------------
	private static Transform FindFaces()
	{
		Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i].name == "Faces" && all[i].gameObject.scene.IsValid() && all[i].root.name.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return all[i];
			}
		}
		return null;
	}

	private string BuildReport()
	{
		int total = SoftSkin.All.Count;
		int drawing = 0;
		int visibleNow = 0;
		int failed = 0;
		StringBuilder fails = new StringBuilder();
		for (int i = 0; i < SoftSkin.All.Count; i++)
		{
			SoftSkin s = SoftSkin.All[i];
			if (s == null)
			{
				continue;
			}
			if (s.Drawing)
			{
				drawing++;
			}
			if (s.Visible)
			{
				visibleNow++;
			}
			string st = s.Status;
			if (st.StartsWith("bone mismatch") || st.StartsWith("weights") || st.StartsWith("ERROR"))
			{
				failed++;
				if (failed <= 3)
				{
					fails.Append("  ").Append(s.Path).Append(": ").Append(st).Append('\n');
				}
			}
		}
		StringBuilder sb = new StringBuilder();
		sb.Append("Scene ").Append(SceneManager.GetActiveScene().name).Append("  speed ").Append(Time.timeScale.ToString("0.##")).Append("x\n");
		sb.Append("Skinned parts: ").Append(total).Append(" active, ").Append(drawing).Append(" drawn by us, ").Append(visibleNow).Append(" on screen, ").Append(failed).Append(" failed\n");
		if (total == 0)
		{
			sb.Append("-> no skinned parts here. Go into gameplay with a character on screen.\n");
		}
		if (failed > 0)
		{
			sb.Append(fails);
		}
		return sb.ToString();
	}

	private static string DescribeSprite(SpriteRenderer r)
	{
		Material m = r.sharedMaterial;
		SortingGroup g = r.GetComponentInParent<SortingGroup>();
		return r.name + ": shader " + (m != null && m.shader != null ? m.shader.name : "?") + ", color " + r.color.ToString("0.00")
			+ ", probes " + r.lightProbeUsage + ", receiveShadows " + r.receiveShadows + ", order " + r.sortingOrder
			+ ", group " + (g != null ? g.name + "(" + g.sortingOrder + ")" : "none") + ", visible " + r.isVisible;
	}

	private string BuildDump()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("===== REPORT ").Append(SceneManager.GetActiveScene().name).Append(" =====\n");
		sb.Append("State: ").Append(SoftSkin.State()).Append('\n');
		sb.Append("Unlit shader available: ").Append(SoftSkin.UnlitAvailable).Append('\n');
		sb.Append("Ambient: mode ").Append(RenderSettings.ambientMode).Append(", light ").Append(RenderSettings.ambientLight.ToString("0.00"))
			.Append(", intensity ").Append(RenderSettings.ambientIntensity.ToString("0.00")).Append('\n');
		sb.Append("Quality: colorSpace ").Append(QualitySettings.activeColorSpace).Append(", pixelLights ").Append(QualitySettings.pixelLightCount)
			.Append(", shadows ").Append(QualitySettings.shadows).Append('\n');

		Light[] lights = FindObjectsOfType<Light>();
		int shown = 0;
		sb.Append("Lights (").Append(lights.Length).Append(" active):\n");
		for (int i = 0; i < lights.Length && shown < 14; i++)
		{
			Light l = lights[i];
			if (!l.enabled)
			{
				continue;
			}
			shown++;
			sb.Append("  ").Append(l.name).Append(": ").Append(l.type).Append(", intensity ").Append(l.intensity.ToString("0.00"))
				.Append(", color ").Append(l.color.ToString("0.00")).Append(", range ").Append(l.range.ToString("0.0"))
				.Append(", shadows ").Append(l.shadows).Append(", mode ").Append(l.renderMode).Append('\n');
		}

		sb.Append("Skinned parts drawn by us (first 5):\n");
		int count = 0;
		for (int i = 0; i < SoftSkin.All.Count && count < 5; i++)
		{
			SoftSkin s = SoftSkin.All[i];
			if (s != null && s.Drawing)
			{
				sb.Append("  ").Append(s.Describe());
				count++;
			}
		}

		sb.Append("Plain (never skinned) sprites for comparison:\n");
		SpriteRenderer[] srs = FindObjectsOfType<SpriteRenderer>();
		int plain = 0;
		for (int i = 0; i < srs.Length && plain < 4; i++)
		{
			string n = srs[i].name;
			if (srs[i].isVisible && (n == "deerHead" || n.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0 || n == "CrowBody"))
			{
				sb.Append("  ").Append(DescribeSprite(srs[i])).Append('\n');
				plain++;
			}
		}

		Transform f = FindFaces();
		if (f != null)
		{
			sb.Append("Player faces (").Append(f.childCount).Append("):\n");
			for (int i = 0; i < f.childCount; i++)
			{
				Transform c = f.GetChild(i);
				SpriteRenderer r = c.GetComponent<SpriteRenderer>();
				sb.Append("  ").Append(c.name).Append(": active ").Append(c.gameObject.activeSelf);
				if (r != null)
				{
					sb.Append(", enabled ").Append(r.enabled).Append(", sprite ").Append(r.sprite != null ? r.sprite.name : "none")
						.Append(", order ").Append(r.sortingOrder).Append(", color ").Append(r.color.ToString("0.00")).Append(", visible ").Append(r.isVisible);
				}
				sb.Append(", localPos ").Append(c.localPosition.ToString("0.00")).Append('\n');
			}
		}
		else
		{
			sb.Append("Player faces: not found (no player in this scene?)\n");
		}
		return sb.ToString();
	}

	// ---------------------------------------------------------------------------------------------
	// drawing
	// ---------------------------------------------------------------------------------------------
	private bool Opt(string label, bool on)
	{
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = on ? new Color(0.4f, 1f, 0.4f) : Color.white;
		bool pressed = GUILayout.Button(label, GUILayout.Height(28f));
		GUI.backgroundColor = old;
		return pressed;
	}

	private void Head(string text)
	{
		GUILayout.Space(6f);
		GUILayout.Label(text, small);
	}

	public void Draw()
	{
		if (small == null)
		{
			small = new GUIStyle(GUI.skin.label);
			small.fontSize = 12;
			small.wordWrap = true;
			small.fontStyle = FontStyle.Bold;
		}
		float s = Mathf.Max(1f, Screen.height / 700f);
		GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
		float w = Mathf.Min(480f, Screen.width / s - 20f);
		float h = Screen.height / s - 20f;
		GUILayout.BeginArea(new Rect(10f, 10f, w, h), GUI.skin.box);
		scroll = GUILayout.BeginScrollView(scroll);

		GUILayout.Label("LOOK LAB  (N closes)");
		GUILayout.Label("Flip a switch, look at the character, then press LOOKS RIGHT or STILL WRONG at the bottom. Finally: Copy log + report.");
		GUILayout.Label(report);

		// ---- 1 ----
		Head("1. SKINNING AND WHICH PARTS");
		GUILayout.BeginHorizontal();
		if (Opt(SoftSkin.Active ? "Skinning ON" : "Skinning OFF", SoftSkin.Active))
		{
			SoftSkin.Active = !SoftSkin.Active;
			Note("skinning " + (SoftSkin.Active ? "ON" : "OFF"));
		}
		if (GUILayout.Button("Reset all switches", GUILayout.Height(28f)))
		{
			SoftSkin.ResetSettings();
			Note("all switches reset");
		}
		GUILayout.EndHorizontal();
		GUILayout.Label("Only skin these parts (the rest stay normal sprites, so you can compare side by side):");
		GUILayout.BeginHorizontal();
		for (int i = 0; i < PartLabels.Length; i++)
		{
			if (Opt(PartLabels[i], SoftSkin.Only == PartKeys[i]))
			{
				SoftSkin.Only = PartKeys[i];
				Note("parts = " + PartLabels[i]);
			}
		}
		GUILayout.EndHorizontal();

		// ---- 2 ----
		Head("2. DARKER / BRIGHTER COLOURS");
		GUILayout.BeginHorizontal();
		GUILayout.Label("Brightness " + SoftSkin.Brightness.ToString("0.00"), GUILayout.Width(110f));
		float nb = GUILayout.HorizontalSlider(SoftSkin.Brightness, 0.4f, 2f);
		if (Mathf.Abs(nb - SoftSkin.Brightness) > 0.004f)
		{
			SoftSkin.Brightness = nb;
			brightDirty = true;
		}
		if (GUILayout.Button("1.00", GUILayout.Width(50f)))
		{
			SoftSkin.Brightness = 1f;
			Note("brightness 1.00");
		}
		GUILayout.EndHorizontal();
		if (brightDirty && Event.current.type == EventType.MouseUp)
		{
			brightDirty = false;
			Note("brightness " + SoftSkin.Brightness.ToString("0.00"));
		}
		GUILayout.Label("Light probes on skinned parts:");
		GUILayout.BeginHorizontal();
		if (Opt("Like sprite", SoftSkin.ProbeMode == 0)) { SoftSkin.ProbeMode = 0; Note("probes = copy sprite"); }
		if (Opt("Off", SoftSkin.ProbeMode == 1)) { SoftSkin.ProbeMode = 1; Note("probes = off"); }
		if (Opt("Blend", SoftSkin.ProbeMode == 2)) { SoftSkin.ProbeMode = 2; Note("probes = blend"); }
		GUILayout.EndHorizontal();
		GUILayout.Label("Receive shadows:");
		GUILayout.BeginHorizontal();
		if (Opt("Like sprite", SoftSkin.ShadowMode == 0)) { SoftSkin.ShadowMode = 0; Note("shadows = copy sprite"); }
		if (Opt("On", SoftSkin.ShadowMode == 1)) { SoftSkin.ShadowMode = 1; Note("shadows = on"); }
		if (Opt("Off", SoftSkin.ShadowMode == 2)) { SoftSkin.ShadowMode = 2; Note("shadows = off"); }
		GUILayout.EndHorizontal();
		GUILayout.Label("Shader (Unlit = no lighting at all; if it looks right, lighting is the cause):");
		GUILayout.BeginHorizontal();
		if (Opt("Lit (original)", !SoftSkin.Unlit)) { SoftSkin.Unlit = false; Note("shader = lit original"); }
		if (Opt("Unlit test", SoftSkin.Unlit)) { SoftSkin.Unlit = true; Note("shader = unlit" + (SoftSkin.UnlitAvailable ? "" : " (NOT AVAILABLE in build)")); }
		GUILayout.EndHorizontal();
		GUILayout.Label("How the colour tint is applied:");
		GUILayout.BeginHorizontal();
		if (Opt("Renderer colour", SoftSkin.TintMode == 0)) { SoftSkin.TintMode = 0; Note("tint = renderer colour"); }
		if (Opt("Vertex colours", SoftSkin.TintMode == 1)) { SoftSkin.TintMode = 1; Note("tint = vertex colours"); }
		if (Opt("No tint", SoftSkin.TintMode == 2)) { SoftSkin.TintMode = 2; Note("tint = none"); }
		GUILayout.EndHorizontal();
		GUILayout.Label("How the texture / colour reaches the shader:");
		GUILayout.BeginHorizontal();
		if (Opt("Property block", !SoftSkin.UseInstance)) { SoftSkin.UseInstance = false; Note("carrier = property block"); }
		if (Opt("Own material copy", SoftSkin.UseInstance)) { SoftSkin.UseInstance = true; Note("carrier = own material"); }
		GUILayout.EndHorizontal();

		// ---- 3 ----
		Head("3. DRAW ORDER / FACE PROBLEMS");
		GUILayout.Label("Where the skinned copy lives (World breaks the character's sorting group, face may vanish):");
		GUILayout.BeginHorizontal();
		if (Opt("Child (normal)", !SoftSkin.WorldSpace)) { SoftSkin.WorldSpace = false; Note("space = child"); }
		if (Opt("World space", SoftSkin.WorldSpace)) { SoftSkin.WorldSpace = true; Note("space = world"); }
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		GUILayout.Label("Draw order offset " + SoftSkin.SortOffset, GUILayout.Width(150f));
		if (GUILayout.Button("-1", GUILayout.Width(45f))) { SoftSkin.SortOffset--; Note("sort offset " + SoftSkin.SortOffset); }
		if (GUILayout.Button("0", GUILayout.Width(45f))) { SoftSkin.SortOffset = 0; Note("sort offset 0"); }
		if (GUILayout.Button("+1", GUILayout.Width(45f))) { SoftSkin.SortOffset++; Note("sort offset " + SoftSkin.SortOffset); }
		GUILayout.EndHorizontal();
		if (Opt(SoftSkin.ShowOriginal ? "Original sprite ALSO shown (overlay)" : "Show original sprite on top too (overlay)", SoftSkin.ShowOriginal))
		{
			SoftSkin.ShowOriginal = !SoftSkin.ShowOriginal;
			Note("overlay " + (SoftSkin.ShowOriginal ? "ON" : "OFF"));
		}
		if (faces != null)
		{
			GUILayout.Label("Show this player face now (the game switches it back by itself):");
			GUILayout.BeginHorizontal();
			for (int i = 0; i < faces.childCount; i++)
			{
				string fname = faces.GetChild(i).name;
				bool on = faces.GetChild(i).gameObject.activeSelf;
				if (Opt(fname, on))
				{
					for (int j = 0; j < faces.childCount; j++)
					{
						faces.GetChild(j).gameObject.SetActive(j == i);
					}
					Note("forced face = " + fname);
				}
			}
			GUILayout.EndHorizontal();
		}
		else
		{
			GUILayout.Label("(no player faces found in this scene)");
		}

		// ---- 4 ----
		Head("4. GAME SPEED");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Freeze", GUILayout.Height(28f))) { Time.timeScale = 0f; }
		if (GUILayout.Button("0.25x", GUILayout.Height(28f))) { Time.timeScale = 0.25f; }
		if (GUILayout.Button("1x", GUILayout.Height(28f))) { Time.timeScale = 1f; }
		GUILayout.EndHorizontal();

		// ---- 5 ----
		Head("5. RESULT LOG  (this is what you send me)");
		GUILayout.BeginHorizontal();
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
		if (GUILayout.Button("LOOKS RIGHT", GUILayout.Height(36f)))
		{
			Note(">>> LOOKS RIGHT with: " + SoftSkin.State());
		}
		GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
		if (GUILayout.Button("STILL WRONG", GUILayout.Height(36f)))
		{
			Note(">>> STILL WRONG with: " + SoftSkin.State());
		}
		GUI.backgroundColor = old;
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Copy log + report", GUILayout.Height(30f)))
		{
			GUIUtility.systemCopyBuffer = logText + "\n" + BuildDump();
			Note("(copied to clipboard)");
		}
		if (GUILayout.Button("Clear log", GUILayout.Height(30f)))
		{
			log.Length = 0;
			logText = "";
		}
		GUILayout.EndHorizontal();
		logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(150f));
		GUILayout.Label(logText, small);
		GUILayout.EndScrollView();

		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}
}

// Separate component so the lab's OnGUI costs nothing while it is closed.
public class FixLabGui : MonoBehaviour
{
	public FixLab owner;

	private void OnGUI()
	{
		if (owner != null)
		{
			owner.Draw();
		}
	}
}
