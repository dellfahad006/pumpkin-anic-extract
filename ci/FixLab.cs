using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Hidden "Fix Lab". Press N to open / close it (M = performance menu, L = cheat menu).
//
// Only one question matters here: do the skinned characters (player hood, monsters, deer, chains) deform
// correctly? Everything on this panel serves that, and updates live while it is open.
//
//   * Big switch: our own skinner (SoftSkin) ON / OFF. Flip it while looking at the deer / monster / player:
//     OFF = the old broken look, ON = the fix. If nothing changes at all, the report below says why.
//   * Speed buttons: freeze or slow the whole game so you can study a pose.
//   * Live report (also copied by "Copy report"), so a screenshot or a paste tells everything.
public class FixLab : MonoBehaviour
{
	private const bool Enabled = true; // set to false to remove the lab from a build

	private static FixLab instance;

	private bool visible;
	private FixLabGui gui;
	private string report = "";
	private float reportTimer;
	private GUIStyle small;

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
		}
		if (visible)
		{
			reportTimer += Time.unscaledDeltaTime;
			if (reportTimer >= 0.5f)
			{
				reportTimer = 0f;
				report = BuildReport();
			}
		}
	}

	// number of ORIGINAL SpriteSkin components alive in the scene (should be 0 after the patch)
	private static int OriginalSkins()
	{
		Type t = Type.GetType("UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation.Runtime");
		if (t == null)
		{
			return -1;
		}
		return FindObjectsOfType(t).Length;
	}

	private string BuildReport()
	{
		int total = SoftSkin.All.Count;
		int drawing = 0;
		int visibleNow = 0;
		int failed = 0;
		StringBuilder fails = new StringBuilder();
		StringBuilder sample = new StringBuilder();
		int shown = 0;
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
				if (shown < 8)
				{
					shown++;
					sample.Append("  ").Append(s.name).Append(": ").Append(s.Status).Append('\n');
				}
			}
			string st = s.Status;
			if (st.StartsWith("bone mismatch") || st.StartsWith("weights") || st.StartsWith("ERROR"))
			{
				failed++;
				if (failed <= 4)
				{
					fails.Append("  ").Append(s.name).Append(": ").Append(st).Append('\n');
				}
			}
		}
		StringBuilder sb = new StringBuilder();
		sb.Append("Scene: ").Append(SceneManager.GetActiveScene().name).Append("   Unity ").Append(Application.unityVersion).Append('\n');
		sb.Append("SoftSkin is ").Append(SoftSkin.Active ? "ON" : "OFF").Append("   light probes ").Append(SoftSkin.UseProbes ? "ON" : "OFF").Append('\n');
		sb.Append("Skinned sprites active in scene: ").Append(total).Append('\n');
		sb.Append("  drawn by our skinner: ").Append(drawing).Append('\n');
		sb.Append("  on screen right now: ").Append(visibleNow).Append('\n');
		sb.Append("  failed (drawn the old way): ").Append(failed).Append('\n');
		int orig = OriginalSkins();
		sb.Append("Original SpriteSkin objects: ").Append(orig < 0 ? "DLL type missing" : orig.ToString()).Append('\n');
		sb.Append("Time.timeScale: ").Append(Time.timeScale.ToString("0.##")).Append('\n');
		if (total == 0)
		{
			sb.Append("-> no skinned sprites here (menu / death screen?). Go into gameplay with a character on screen.\n");
		}
		if (failed > 0)
		{
			sb.Append("First failures:\n").Append(fails);
		}
		if (sample.Length > 0)
		{
			sb.Append("On screen:\n").Append(sample);
		}
		return sb.ToString();
	}

	public void Draw()
	{
		if (small == null)
		{
			small = new GUIStyle(GUI.skin.label);
			small.fontSize = 12;
			small.wordWrap = true;
		}
		float s = Mathf.Max(1f, Screen.height / 700f);
		GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
		float w = Mathf.Min(430f, Screen.width / s - 20f);
		float h = Mathf.Min(560f, Screen.height / s - 20f);
		GUILayout.BeginArea(new Rect(10f, 10f, w, h), GUI.skin.box);
		GUILayout.Label("FIX LAB  (N closes)  -  characters not deforming?");

		Color old = GUI.backgroundColor;
		GUI.backgroundColor = SoftSkin.Active ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.5f, 0.5f);
		if (GUILayout.Button(SoftSkin.Active ? "Software skinning: ON   (click to turn OFF)" : "Software skinning: OFF  (click to turn ON)", GUILayout.Height(44f)))
		{
			SoftSkin.Active = !SoftSkin.Active;
			reportTimer = 99f;
		}
		GUI.backgroundColor = SoftSkin.UseProbes ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.8f, 0.4f);
		if (GUILayout.Button(SoftSkin.UseProbes ? "Light probes on skinned parts: ON (like original sprites)" : "Light probes on skinned parts: OFF (test)", GUILayout.Height(30f)))
		{
			SoftSkin.UseProbes = !SoftSkin.UseProbes;
		}
		GUI.backgroundColor = old;

		GUILayout.Label("Game speed (to study a pose):", small);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Freeze", GUILayout.Height(30f)))
		{
			Time.timeScale = 0f;
		}
		if (GUILayout.Button("0.25x", GUILayout.Height(30f)))
		{
			Time.timeScale = 0.25f;
		}
		if (GUILayout.Button("1x (normal)", GUILayout.Height(30f)))
		{
			Time.timeScale = 1f;
		}
		GUILayout.EndHorizontal();

		GUILayout.Label(report, small);

		if (GUILayout.Button("Copy report", GUILayout.Height(28f)))
		{
			GUIUtility.systemCopyBuffer = report;
		}
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
