using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// Hidden "Fix Lab". Press N to open / close it (the M key is the performance menu).
//
// Purpose: find out WHY the skinned characters (player hood, monsters, deer, chains) look wrong in the
// WebGL build, and try candidate fixes live, without rebuilding for every experiment.
//
// Everything about the animation DLL is reached by reflection, so this file compiles even if the DLL's
// internals differ from what I expect. "Dump API" prints what is really inside it.
public class FixLab : MonoBehaviour
{
	private const bool Enabled = true; // set to false to remove the lab from a build
	private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
	private const string SkinTypeName = "UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation.Runtime";

	private static FixLab instance;

	private bool visible;
	private FixLabGui gui;
	private readonly StringBuilder log = new StringBuilder();
	private string logText = "";
	private Vector2 logScroll;
	private Vector2 pageScroll;
	private string animFilter = "monster,deer,wendigo,player,ghoul";
	private string methodName = "Deform";
	private bool liveMonitor;
	private float monitorTimer;
	private string monitorText = "";
	private GUIStyle noteStyle;

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
			if (visible && log.Length == 0)
			{
				Log("Fix Lab ready. Unity " + Application.unityVersion + ", " + Application.platform + ", graphics " + SystemInfo.graphicsDeviceType);
			}
		}
		if (visible && liveMonitor)
		{
			monitorTimer += Time.unscaledDeltaTime;
			if (monitorTimer >= 0.5f)
			{
				monitorTimer = 0f;
				monitorText = BuildAnimatorLines(18);
			}
		}
	}

	// ============================================================================================
	// helpers
	// ============================================================================================
	private void Log(string line)
	{
		log.Append(line).Append('\n');
		if (log.Length > 9000)
		{
			log.Remove(0, log.Length - 6000);
		}
		logText = log.ToString();
	}

	private static Type SkinType()
	{
		return Type.GetType(SkinTypeName);
	}

	private UnityEngine.Object[] Skins(out Type t)
	{
		t = SkinType();
		if (t == null)
		{
			Log("!! SpriteSkin type NOT FOUND (animation DLL missing or stripped from this build).");
			return new UnityEngine.Object[0];
		}
		return FindObjectsOfType(t);
	}

	private static object Read(PropertyInfo p, object o)
	{
		if (p == null)
		{
			return "n/a";
		}
		try
		{
			return p.GetValue(o, null);
		}
		catch (Exception e)
		{
			return "ERR:" + e.GetType().Name;
		}
	}

	// ============================================================================================
	// skinning
	// ============================================================================================
	public void SkinCheck()
	{
		Type t;
		UnityEngine.Object[] skins = Skins(out t);
		if (t == null)
		{
			return;
		}
		PropertyInfo pValid = t.GetProperty("isValid", Inst);
		PropertyInfo pBatch = t.GetProperty("batchSkinning", Inst);
		PropertyInfo pAlways = t.GetProperty("alwaysUpdate", Inst);
		PropertyInfo pBones = t.GetProperty("boneTransforms", Inst);
		int valid = 0;
		int invalid = 0;
		int batched = 0;
		for (int i = 0; i < skins.Length; i++)
		{
			object v = Read(pValid, skins[i]);
			if (v is bool && (bool)v)
			{
				valid++;
			}
			else if (v is bool)
			{
				invalid++;
			}
			object b = Read(pBatch, skins[i]);
			if (b is bool && (bool)b)
			{
				batched++;
			}
		}
		Log(string.Format("SKIN CHECK: {0} active skins, valid {1}, INVALID {2}, batched {3}. Properties: isValid {4}, batchSkinning {5}, alwaysUpdate {6}, boneTransforms {7}", skins.Length, valid, invalid, batched, pValid != null, pBatch != null, pAlways != null, pBones != null));
		for (int i = 0; i < skins.Length && i < 14; i++)
		{
			Behaviour beh = skins[i] as Behaviour;
			SpriteRenderer sr = beh != null ? beh.GetComponent<SpriteRenderer>() : null;
			Transform[] bt = Read(pBones, skins[i]) as Transform[];
			Log(string.Format("  {0}: enabled {1}, valid {2}, batch {3}, always {4}, bones {5}, sprite {6}, visible {7}", skins[i].name, beh != null && beh.enabled, Read(pValid, skins[i]), Read(pBatch, skins[i]), Read(pAlways, skins[i]), bt != null ? bt.Length.ToString() : "?", sr != null && sr.sprite != null ? sr.sprite.name : "?", sr != null && sr.isVisible));
		}
	}

	public void RestartSkins()
	{
		Type t;
		UnityEngine.Object[] skins = Skins(out t);
		for (int i = 0; i < skins.Length; i++)
		{
			Behaviour b = skins[i] as Behaviour;
			if (b != null)
			{
				b.enabled = false;
				b.enabled = true;
			}
		}
		Log("Restarted " + skins.Length + " skins (disabled then enabled).");
	}

	public void SetSkinBool(string property, bool value)
	{
		Type t;
		UnityEngine.Object[] skins = Skins(out t);
		if (t == null)
		{
			return;
		}
		PropertyInfo p = t.GetProperty(property, Inst);
		if (p == null || !p.CanWrite)
		{
			Log("!! " + property + " cannot be set (property missing or read-only in this DLL version).");
			return;
		}
		int n = 0;
		for (int i = 0; i < skins.Length; i++)
		{
			try
			{
				p.SetValue(skins[i], value, null);
				n++;
			}
			catch (Exception e)
			{
				Log("!! " + property + " failed: " + e.GetType().Name);
				break;
			}
		}
		Log(property + " = " + value + " on " + n + " skins. Now press Restart skins and watch the characters.");
	}

	public void InvokeOnSkins(string method)
	{
		Type t;
		UnityEngine.Object[] skins = Skins(out t);
		if (t == null)
		{
			return;
		}
		MethodInfo mi = t.GetMethod(method.Trim(), Inst);
		if (mi == null)
		{
			Log("!! No method named '" + method.Trim() + "' on SpriteSkin. Use Dump API to see the real names.");
			return;
		}
		if (mi.GetParameters().Length != 0)
		{
			Log("!! '" + method.Trim() + "' takes parameters, only parameterless methods can be called here.");
			return;
		}
		int ok = 0;
		for (int i = 0; i < skins.Length; i++)
		{
			try
			{
				mi.Invoke(skins[i], null);
				ok++;
			}
			catch (Exception e)
			{
				Log("!! " + method.Trim() + " threw " + e.GetType().Name + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message));
				break;
			}
		}
		Log("Invoked " + method.Trim() + " on " + ok + " skins.");
	}

	private void AppendMembers(Type t)
	{
		MemberInfo[] ms = t.GetMembers(Declared);
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < ms.Length; i++)
		{
			string n = ms[i].Name;
			if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") || n.StartsWith("remove_") || n.StartsWith("<") || n == ".ctor" || n == ".cctor")
			{
				continue;
			}
			string kind = ms[i].MemberType == MemberTypes.Method ? "M:" : (ms[i].MemberType == MemberTypes.Property ? "P:" : (ms[i].MemberType == MemberTypes.Field ? "F:" : "?:"));
			sb.Append(kind).Append(n).Append(' ');
		}
		Log(t.FullName + " -> " + sb);
	}

	public void DumpApi()
	{
		Type t = SkinType();
		if (t == null)
		{
			Log("!! SpriteSkin type NOT FOUND.");
			return;
		}
		AppendMembers(t);
		Type[] types;
		try
		{
			types = t.Assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			types = e.Types;
			Log("(some types in the animation DLL failed to load - that alone can break skinning)");
		}
		StringBuilder related = new StringBuilder();
		int shown = 0;
		for (int i = 0; i < types.Length; i++)
		{
			if (types[i] == null)
			{
				continue;
			}
			string n = types[i].Name;
			if (n.Contains("Composite") || n.Contains("Deform") || n.Contains("Updater") || n.Contains("Helper") || n.Contains("Manager") || n.Contains("Batch"))
			{
				related.Append(types[i].FullName).Append(' ');
				shown++;
				if (n.Contains("Composite") && shown < 4)
				{
					AppendMembers(types[i]);
				}
			}
		}
		Log("Related types (" + shown + "): " + related);
	}

	// ============================================================================================
	// animators
	// ============================================================================================
	private bool Matches(Animator a)
	{
		string[] keys = animFilter.Split(',');
		string ctrl = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "";
		string n = a.gameObject.name;
		for (int i = 0; i < keys.Length; i++)
		{
			string k = keys[i].Trim();
			if (k.Length == 0)
			{
				continue;
			}
			if (ctrl.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static string ClipName(Animator a)
	{
		if (!a.isActiveAndEnabled || a.runtimeAnimatorController == null)
		{
			return "-";
		}
		if (!a.isInitialized)
		{
			return "(not initialised)";
		}
		AnimatorClipInfo[] ci = a.GetCurrentAnimatorClipInfo(0);
		return ci.Length > 0 && ci[0].clip != null ? ci[0].clip.name : "(none)";
	}

	private string BuildAnimatorLines(int max)
	{
		Animator[] all = FindObjectsOfType<Animator>();
		StringBuilder sb = new StringBuilder();
		int n = 0;
		for (int i = 0; i < all.Length && n < max; i++)
		{
			Animator a = all[i];
			if (!Matches(a))
			{
				continue;
			}
			n++;
			sb.AppendFormat("{0} [{1}]  clip: {2}  speed {3:0.0}  enabled {4}  cull {5}\n", a.gameObject.name, a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "no controller", ClipName(a), a.speed, a.enabled, a.cullingMode);
		}
		if (n == 0)
		{
			sb.Append("(no active animators match the filter)\n");
		}
		return sb.ToString();
	}

	public void ListAnimators()
	{
		Log("ANIMATORS matching '" + animFilter + "':\n" + BuildAnimatorLines(30));
	}

	public void RebindFiltered()
	{
		Animator[] all = FindObjectsOfType<Animator>();
		int n = 0;
		for (int i = 0; i < all.Length; i++)
		{
			if (Matches(all[i]))
			{
				all[i].Rebind();
				n++;
			}
		}
		Log("Rebound " + n + " animators matching the filter.");
	}

	public void AlwaysAnimateAll()
	{
		Animator[] all = FindObjectsOfType<Animator>();
		for (int i = 0; i < all.Length; i++)
		{
			all[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
		}
		Log("Culling mode set to AlwaysAnimate on " + all.Length + " animators.");
	}

	// ============================================================================================
	// drawing
	// ============================================================================================
	public void Draw()
	{
		if (noteStyle == null)
		{
			noteStyle = new GUIStyle(GUI.skin.label);
			noteStyle.fontSize = 11;
			noteStyle.wordWrap = true;
		}
		float s = Mathf.Max(1f, Screen.height / 700f);
		GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
		float w = Mathf.Min(620f, Screen.width / s - 20f);
		float h = Screen.height / s - 20f;
		GUILayout.BeginArea(new Rect(10f, 10f, w, h), GUI.skin.box);
		pageScroll = GUILayout.BeginScrollView(pageScroll);

		GUILayout.Label("FIX LAB  (press N to close)   - try a method, watch the characters, read the log below.");

		GUILayout.Label("1. SKINNING (hood / monster / deer bodies not following their bones)", noteStyle);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Skin check"))
		{
			SkinCheck();
		}
		if (GUILayout.Button("Dump API"))
		{
			DumpApi();
		}
		if (GUILayout.Button("Restart skins"))
		{
			RestartSkins();
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Batching ON"))
		{
			SetSkinBool("batchSkinning", true);
		}
		if (GUILayout.Button("Batching OFF"))
		{
			SetSkinBool("batchSkinning", false);
		}
		if (GUILayout.Button("Always update ON"))
		{
			SetSkinBool("alwaysUpdate", true);
		}
		if (GUILayout.Button("Always update OFF"))
		{
			SetSkinBool("alwaysUpdate", false);
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		GUILayout.Label("Method:", GUILayout.Width(55f));
		methodName = GUILayout.TextField(methodName);
		if (GUILayout.Button("Invoke on all skins", GUILayout.Width(150f)))
		{
			InvokeOnSkins(methodName);
		}
		GUILayout.EndHorizontal();

		GUILayout.Label("2. ANIMATORS (monster / deer look stiff or glide; which clip is really playing?)", noteStyle);
		GUILayout.BeginHorizontal();
		GUILayout.Label("Filter:", GUILayout.Width(45f));
		animFilter = GUILayout.TextField(animFilter);
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("List animators"))
		{
			ListAnimators();
		}
		if (GUILayout.Button("Rebind filtered"))
		{
			RebindFiltered();
		}
		if (GUILayout.Button("All always animate"))
		{
			AlwaysAnimateAll();
		}
		GUI.backgroundColor = liveMonitor ? new Color(0.4f, 1f, 0.4f) : Color.white;
		if (GUILayout.Button(liveMonitor ? "Live monitor ON" : "Live monitor off"))
		{
			liveMonitor = !liveMonitor;
			monitorText = liveMonitor ? BuildAnimatorLines(18) : "";
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
		if (liveMonitor)
		{
			GUILayout.Label(monitorText, noteStyle);
		}

		GUILayout.Label("3. LOG", noteStyle);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Clear log"))
		{
			log.Length = 0;
			logText = "";
		}
		if (GUILayout.Button("Copy log"))
		{
			GUIUtility.systemCopyBuffer = logText;
		}
		GUILayout.EndHorizontal();
		logScroll = GUILayout.BeginScrollView(logScroll, GUILayout.Height(260f));
		GUILayout.Label(logText, noteStyle);
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
