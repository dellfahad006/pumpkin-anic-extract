using TMPro;
using UnityEngine;

// "Experimental" menu. Press I to open / close it. (M = performance menu, N = Look Lab, L = cheat menu.)
//
// Home for things that are tried out here first before they become normal behaviour. Every switch is remembered in
// this browser (PlayerPrefs), and nothing runs while a switch is off.
//
//   * Skip-tutorial hint: still + smaller. The "SKIP TUTORIAL [..]" text normally pulses its colour and sways
//     (one Animator on the SkipTutorialText object). When this is on, that animator is switched off, the text is
//     reset to plain white with no rotation, and it is drawn smaller.
public class ExperimentalMenu : MonoBehaviour
{
	private const bool Enabled = true; // set to false to remove the menu from a build

	private const string PrefStill = "pp_exp_skipStill";
	private const string PrefSize = "pp_exp_skipSize";

	private static ExperimentalMenu instance;

	private bool visible;
	private ExperimentalMenuGui gui;
	private bool skipStill;
	private float skipSize = 0.75f;
	private bool sizeDirty;
	private bool applied;
	private float tick;
	private GUIStyle small;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		if (!Enabled || instance != null)
		{
			return;
		}
		GameObject go = new GameObject("ExperimentalMenu");
		DontDestroyOnLoad(go);
		instance = go.AddComponent<ExperimentalMenu>();
	}

	private void Awake()
	{
		skipStill = PlayerPrefs.GetInt(PrefStill, 0) == 1;
		skipSize = Mathf.Clamp(PlayerPrefs.GetFloat(PrefSize, 0.75f), 0.4f, 1f);
		gui = gameObject.AddComponent<ExperimentalMenuGui>();
		gui.owner = this;
		gui.enabled = false;
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.I))
		{
			visible = !visible;
			gui.enabled = visible;
		}
		tick += Time.unscaledDeltaTime;
		if (tick >= 0.25f)
		{
			tick = 0f;
			ApplySkipHint();
		}
	}

	// Keeps the hint text in the wanted state; the text object can appear / reappear at any time (new scene, new run).
	private void ApplySkipHint()
	{
		if (!skipStill && !applied)
		{
			return;
		}
		Animator[] all = Resources.FindObjectsOfTypeAll<Animator>();
		for (int i = 0; i < all.Length; i++)
		{
			Animator a = all[i];
			if (a == null || a.name != "SkipTutorialText" || !a.gameObject.scene.IsValid())
			{
				continue;
			}
			RectTransform rt = a.transform as RectTransform;
			if (skipStill)
			{
				if (a.enabled)
				{
					a.enabled = false;
				}
				if (rt != null)
				{
					rt.localRotation = Quaternion.identity;
					rt.localScale = Vector3.one * skipSize;
				}
				TMP_Text t = a.GetComponent<TMP_Text>();
				if (t != null)
				{
					t.color = Color.white;
				}
			}
			else
			{
				a.enabled = true;
				if (rt != null)
				{
					rt.localScale = Vector3.one;
				}
			}
		}
		applied = skipStill;
	}

	private void SetStill(bool value)
	{
		skipStill = value;
		PlayerPrefs.SetInt(PrefStill, value ? 1 : 0);
		PlayerPrefs.Save();
		tick = 99f;
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
		float w = Mathf.Min(400f, Screen.width / s - 20f);
		float h = Mathf.Min(260f, Screen.height / s - 20f);
		GUILayout.BeginArea(new Rect((Screen.width / s - w) / 2f, 10f, w, h), GUI.skin.box);
		GUILayout.Label("EXPERIMENTAL  (I closes)");
		GUILayout.Label("Things being tried out. Each switch is remembered in this browser.", small);

		Color old = GUI.backgroundColor;
		GUI.backgroundColor = skipStill ? new Color(0.4f, 1f, 0.4f) : Color.white;
		if (GUILayout.Button((skipStill ? "[ON]  " : "[off] ") + "Skip-tutorial hint: still + smaller", GUILayout.Height(34f)))
		{
			SetStill(!skipStill);
		}
		GUI.backgroundColor = old;

		GUILayout.BeginHorizontal();
		GUILayout.Label("Hint size " + skipSize.ToString("0.00"), GUILayout.Width(110f));
		float ns = GUILayout.HorizontalSlider(skipSize, 0.4f, 1f);
		if (Mathf.Abs(ns - skipSize) > 0.004f)
		{
			skipSize = ns;
			sizeDirty = true;
			tick = 99f;
		}
		GUILayout.EndHorizontal();
		if (sizeDirty && Event.current.type == EventType.MouseUp)
		{
			sizeDirty = false;
			PlayerPrefs.SetFloat(PrefSize, skipSize);
			PlayerPrefs.Save();
		}
		GUILayout.Label("Stops the SKIP TUTORIAL text from swaying / pulsing and makes it smaller. It only shows at the start of a run, when skipping is allowed.", small);
		GUILayout.EndArea();
	}
}

// Separate component so the menu's OnGUI costs nothing while it is closed.
public class ExperimentalMenuGui : MonoBehaviour
{
	public ExperimentalMenu owner;

	private void OnGUI()
	{
		if (owner != null)
		{
			owner.Draw();
		}
	}
}
