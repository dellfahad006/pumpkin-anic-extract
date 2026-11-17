using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Hidden developer menu. Press M to open / close it. Nothing is created on screen, and no per-frame
// work is done, until M is pressed. Options are NOT saved: reloading the page resets everything.
//
// What it gives you:
//   * live FPS, frame time, "work" time (CPU time the game needs per frame, which keeps showing
//     savings even when the browser caps you at 60 FPS) and several memory numbers
//   * switches for performance ideas that players WOULD notice
//   * a "Test" button on every switch: measures 3 s with it in the current state, 3 s flipped,
//     puts it back and prints the difference, so you get real numbers for your own machine
[DefaultExecutionOrder(-32000)]
public class DevMenu : MonoBehaviour
{
	private const bool Enabled = true; // set to false to remove the menu from a build

#if UNITY_WEBGL && !UNITY_EDITOR
	[DllImport("__Internal")]
	private static extern float DevMenu_HeapMB();

	[DllImport("__Internal")]
	private static extern float DevMenu_JSHeapMB();

	[DllImport("__Internal")]
	private static extern float DevMenu_CanvasW();

	[DllImport("__Internal")]
	private static extern float DevMenu_CanvasH();

	[DllImport("__Internal")]
	private static extern void DevMenu_SetRetro(int on, int cssW, int cssH);
#else
	private static float DevMenu_HeapMB()
	{
		return 0f;
	}

	private static float DevMenu_JSHeapMB()
	{
		return 0f;
	}

	private static float DevMenu_CanvasW()
	{
		return 0f;
	}

	private static float DevMenu_CanvasH()
	{
		return 0f;
	}

	private static void DevMenu_SetRetro(int on, int cssW, int cssH)
	{
	}
#endif

	public class Option
	{
		public string name;
		public string note;
		public bool on;
		public Action<bool> apply;
		public Action rescan;
		public string result;
		public bool excludeFromAll;
	}

	private static DevMenu instance;

	private readonly List<Option> options = new List<Option>();
	private Option allOption;
	private Option smallShadowOption;
	private bool visible;
	private DevMenuGui gui;

	// ---- timing ----
	private long frameStartTicks;
	private const int RingSize = 240;
	private readonly float[] dtRing = new float[RingSize];
	private readonly float[] workRing = new float[RingSize];
	private int ringIndex;
	private int ringCount;
	private Coroutine frameLoop;
	private readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

	// ---- A/B measuring ----
	private bool measuring;
	private float sumDt;
	private float sumWork;
	private int sumFrames;
	private float avgDt;
	private float avgWork;
	private bool testing;

	// ---- text shown ----
	private string statsText = "";
	private string countsText = "";
	private string gcText = "";
	private float statsTimer = 99f;
	private float rescanTimer;

	// ---- saved values to restore ----
	private ShadowQuality savedShadows;
	private float savedShadowDistance;
	private AnisotropicFiltering savedAniso;
	private int savedTextureLimit;
	private int savedAA;
	private int savedPixelLights;
	private float savedFixedDelta;
	private float savedFar;

	private readonly Dictionary<Renderer, ShadowCastingMode> shadowOriginal = new Dictionary<Renderer, ShadowCastingMode>();
	private readonly HashSet<ParticleSystem> particlesOff = new HashSet<ParticleSystem>();
	private readonly List<Animator> grassOff = new List<Animator>();

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		if (!Enabled || instance != null)
		{
			return;
		}
		GameObject go = new GameObject("DevMenu");
		DontDestroyOnLoad(go);
		instance = go.AddComponent<DevMenu>();
	}

	private void Awake()
	{
		BuildOptions();
		gui = gameObject.AddComponent<DevMenuGui>();
		gui.owner = this;
		gui.enabled = false;
	}

	// ============================================================================================
	// options
	// ============================================================================================
	private Option Add(string name, string note, Action<bool> apply)
	{
		Option o = new Option();
		o.name = name;
		o.note = note;
		o.apply = apply;
		options.Add(o);
		return o;
	}

	private void BuildOptions()
	{
		allOption = new Option();
		allOption.name = "ALL of the options below at once";
		allOption.note = "Shows the total potential. Use Test to see the combined saving.";
		allOption.apply = delegate(bool v)
		{
			for (int i = 0; i < options.Count; i++)
			{
				if (options[i] != allOption && !options[i].excludeFromAll)
				{
					SetOption(options[i], v);
				}
			}
		};
		options.Add(allOption);

		Add("Shadows off", "Players see: no shadows anywhere. Usually the biggest GPU saving.", delegate(bool v)
		{
			if (v)
			{
				savedShadows = QualitySettings.shadows;
				QualitySettings.shadows = ShadowQuality.Disable;
			}
			else
			{
				QualitySettings.shadows = savedShadows;
			}
		});

		Add("Shadow distance 15", "Players see: shadows disappear a short way from the camera.", delegate(bool v)
		{
			if (v)
			{
				savedShadowDistance = QualitySettings.shadowDistance;
				QualitySettings.shadowDistance = 15f;
			}
			else
			{
				QualitySettings.shadowDistance = savedShadowDistance;
			}
		});

		smallShadowOption = Add("Small objects cast no shadow", "Players see: plants, flowers and small props have no shadow; trees and buildings keep theirs.", delegate(bool v)
		{
			if (v)
			{
				ScanSmallShadows();
			}
			else
			{
				foreach (KeyValuePair<Renderer, ShadowCastingMode> kv in shadowOriginal)
				{
					if (kv.Key != null)
					{
						kv.Key.shadowCastingMode = kv.Value;
					}
				}
				shadowOriginal.Clear();
			}
		});
		smallShadowOption.rescan = ScanSmallShadows;

		Option particles = Add("Particles off", "Players see: no dust, sparkles, splashes or other particle effects.", delegate(bool v)
		{
			if (v)
			{
				ScanParticles();
			}
			else
			{
				foreach (ParticleSystem ps in particlesOff)
				{
					if (ps != null)
					{
						ParticleSystem.EmissionModule em = ps.emission;
						em.enabled = true;
					}
				}
				particlesOff.Clear();
			}
		});
		particles.rescan = ScanParticles;

		Option grass = Add("Grass stops swaying", "Players see: the grass stands still instead of moving in the wind.", delegate(bool v)
		{
			if (v)
			{
				ScanGrass();
			}
			else
			{
				for (int i = 0; i < grassOff.Count; i++)
				{
					if (grassOff[i] != null)
					{
						grassOff[i].enabled = true;
					}
				}
				grassOff.Clear();
			}
		});
		grass.rescan = ScanGrass;

		Option skins = Add("Characters update only when on screen", "Players see: nothing normally; characters at the very screen edge could pop in late.", delegate(bool v)
		{
			SetSkinsAlwaysUpdate(!v);
		});
		skins.rescan = delegate
		{
			SetSkinsAlwaysUpdate(false);
		};

		Add("Audio engine paused", "Players see: total silence. Diagnostic: shows how much the sound mixing costs.", delegate(bool v)
		{
			AudioListener.pause = v;
		});

		Add("Frame cap 30 FPS", "Players see: choppier motion, but the CPU and GPU do half the work.", delegate(bool v)
		{
			Application.targetFrameRate = v ? 30 : -1;
		});

		Add("Physics 25 times per second", "Players see: usually nothing; movement collisions are slightly less precise.", delegate(bool v)
		{
			if (v)
			{
				savedFixedDelta = Time.fixedDeltaTime;
				Time.fixedDeltaTime = 0.04f;
			}
			else
			{
				Time.fixedDeltaTime = savedFixedDelta;
			}
		});

		Add("Camera sees only 150 units", "Players see: very distant scenery could vanish.", delegate(bool v)
		{
			Camera cam = Camera.main;
			if (cam == null)
			{
				return;
			}
			if (v)
			{
				savedFar = cam.farClipPlane;
				cam.farClipPlane = 150f;
			}
			else
			{
				cam.farClipPlane = savedFar;
			}
		});

		Add("Pixel lights: 1", "Players see: only the strongest light is calculated per pixel; others look flatter.", delegate(bool v)
		{
			if (v)
			{
				savedPixelLights = QualitySettings.pixelLightCount;
				QualitySettings.pixelLightCount = 1;
			}
			else
			{
				QualitySettings.pixelLightCount = savedPixelLights;
			}
		});

		Add("Anisotropic filtering off", "Players see: textures slightly blurrier at steep angles.", delegate(bool v)
		{
			if (v)
			{
				savedAniso = QualitySettings.anisotropicFiltering;
				QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
			}
			else
			{
				QualitySettings.anisotropicFiltering = savedAniso;
			}
		});

		Add("Anti-aliasing off", "Players see: slightly jagged edges. Only matters on the High quality level.", delegate(bool v)
		{
			if (v)
			{
				savedAA = QualitySettings.antiAliasing;
				QualitySettings.antiAliasing = 0;
			}
			else
			{
				QualitySettings.antiAliasing = savedAA;
			}
		});

		Add("Textures half resolution", "Players see: blurrier textures. Cuts texture memory for textures that have mipmaps. Causes one short hitch when switched.", delegate(bool v)
		{
			if (v)
			{
				savedTextureLimit = QualitySettings.masterTextureLimit;
				QualitySettings.masterTextureLimit = 1;
			}
			else
			{
				QualitySettings.masterTextureLimit = savedTextureLimit;
			}
		});

		retroOption = Add("RETRO pixel mode (renders far fewer pixels)", "Players see: a chunky, pixelated picture (size set below). The browser draws fewer pixels, so it can save GPU work. Not part of 'ALL'.", delegate(bool v)
		{
			ApplyRetro(v);
		});
		retroOption.excludeFromAll = true;
	}

	// ---- retro pixel mode ----
	// Lowers the size of the drawing buffer and tells the browser to enlarge it with hard pixel edges
	// instead of smoothing. This genuinely reduces GPU work (fill rate), unlike a post-effect filter.
	private Option retroOption;
	private int retroFactor = 3;
	private bool retroActive;
	private int retroOrigW;
	private int retroOrigH;
	private int retroCssW;
	private int retroCssH;

	private void ApplyRetro(bool on)
	{
		if (on)
		{
			if (!retroActive)
			{
				retroOrigW = Screen.width;
				retroOrigH = Screen.height;
				retroCssW = (int)DevMenu_CanvasW();
				retroCssH = (int)DevMenu_CanvasH();
				if (retroCssW <= 0 || retroCssH <= 0)
				{
					retroCssW = retroOrigW;
					retroCssH = retroOrigH;
				}
			}
			retroActive = true;
		}
		else
		{
			retroActive = false;
		}
		StartCoroutine(RetroRoutine(on));
	}

	private IEnumerator RetroRoutine(bool on)
	{
		if (on)
		{
			int w = Mathf.Max(160, retroOrigW / retroFactor);
			int h = Mathf.Max(100, retroOrigH / retroFactor);
			DevMenu_SetRetro(1, retroCssW, retroCssH);
			Screen.SetResolution(w, h, Screen.fullScreen);
			// Unity can touch the canvas right after a resolution change, so set our style a few times
			for (int i = 0; i < 3; i++)
			{
				yield return new WaitForSecondsRealtime(0.2f);
				if (!retroActive)
				{
					yield break;
				}
				DevMenu_SetRetro(1, retroCssW, retroCssH);
			}
		}
		else
		{
			DevMenu_SetRetro(0, 0, 0);
			Screen.SetResolution(retroOrigW, retroOrigH, Screen.fullScreen);
		}
	}

	private void SetOption(Option o, bool value)
	{
		if (o.on == value)
		{
			return;
		}
		o.on = value;
		try
		{
			o.apply(value);
		}
		catch (Exception e)
		{
			o.result = "error: " + e.Message;
		}
	}

	private void ScanSmallShadows()
	{
		MeshRenderer[] all = FindObjectsOfType<MeshRenderer>();
		for (int i = 0; i < all.Length; i++)
		{
			MeshRenderer r = all[i];
			if (r.shadowCastingMode == ShadowCastingMode.Off || shadowOriginal.ContainsKey(r))
			{
				continue;
			}
			if (r.bounds.size.sqrMagnitude < 4f)
			{
				shadowOriginal[r] = r.shadowCastingMode;
				r.shadowCastingMode = ShadowCastingMode.Off;
			}
		}
	}

	private void ScanParticles()
	{
		ParticleSystem[] all = FindObjectsOfType<ParticleSystem>();
		for (int i = 0; i < all.Length; i++)
		{
			ParticleSystem ps = all[i];
			ParticleSystem.EmissionModule em = ps.emission;
			if (em.enabled && !particlesOff.Contains(ps))
			{
				em.enabled = false;
				particlesOff.Add(ps);
			}
		}
	}

	private void ScanGrass()
	{
		Animator[] all = FindObjectsOfType<Animator>();
		for (int i = 0; i < all.Length; i++)
		{
			Animator a = all[i];
			if (a.enabled && a.runtimeAnimatorController != null && a.runtimeAnimatorController.name == "Grass2")
			{
				a.enabled = false;
				grassOff.Add(a);
			}
		}
	}

	// SpriteSkin lives in a precompiled DLL; reach it by reflection so this file never fails to compile.
	private void SetSkinsAlwaysUpdate(bool alwaysUpdate)
	{
		Type t = Type.GetType("UnityEngine.U2D.Animation.SpriteSkin, Unity.2D.Animation.Runtime");
		if (t == null)
		{
			return;
		}
		PropertyInfo pi = t.GetProperty("alwaysUpdate");
		if (pi == null)
		{
			return;
		}
		UnityEngine.Object[] skins = FindObjectsOfType(t);
		for (int i = 0; i < skins.Length; i++)
		{
			pi.SetValue(skins[i], alwaysUpdate, null);
		}
	}

	// ============================================================================================
	// per-frame
	// ============================================================================================
	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.M))
		{
			SetVisible(!visible);
		}

		// options that must follow objects spawned later (new plants, effects, ...)
		rescanTimer += Time.unscaledDeltaTime;
		if (rescanTimer > 3f)
		{
			rescanTimer = 0f;
			for (int i = 0; i < options.Count; i++)
			{
				if (options[i].on && options[i].rescan != null && options[i] != allOption)
				{
					options[i].rescan();
				}
			}
		}

		if (!visible)
		{
			return;
		}
		frameStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
		statsTimer += Time.unscaledDeltaTime;
		if (statsTimer >= 0.5f)
		{
			statsTimer = 0f;
			RebuildStats();
		}
	}

	private void SetVisible(bool value)
	{
		visible = value;
		gui.enabled = value;
		if (value)
		{
			ringCount = 0;
			ringIndex = 0;
			statsTimer = 99f;
			frameLoop = StartCoroutine(FrameLoop());
		}
		else if (frameLoop != null)
		{
			StopCoroutine(frameLoop);
			frameLoop = null;
		}
	}

	// runs after rendering has been submitted each frame
	private IEnumerator FrameLoop()
	{
		while (true)
		{
			yield return endOfFrame;
			float workMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - frameStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
			float dt = Time.unscaledDeltaTime;
			dtRing[ringIndex] = dt;
			workRing[ringIndex] = workMs;
			ringIndex = (ringIndex + 1) % RingSize;
			if (ringCount < RingSize)
			{
				ringCount++;
			}
			if (measuring)
			{
				sumDt += dt;
				sumWork += workMs;
				sumFrames++;
			}
		}
	}

	private void RebuildStats()
	{
		float dtSum = 0f;
		float workSum = 0f;
		float dtMax = 0f;
		float workMax = 0f;
		for (int i = 0; i < ringCount; i++)
		{
			dtSum += dtRing[i];
			workSum += workRing[i];
			if (dtRing[i] > dtMax)
			{
				dtMax = dtRing[i];
			}
			if (workRing[i] > workMax)
			{
				workMax = workRing[i];
			}
		}
		float n = Mathf.Max(1, ringCount);
		float dtAvg = dtSum / n;
		float fps = dtAvg > 0f ? 1f / dtAvg : 0f;

		StringBuilder sb = new StringBuilder();
		sb.AppendFormat("FPS {0:0.0}   frame {1:0.0} ms (worst {2:0.0})\n", fps, dtAvg * 1000f, dtMax * 1000f);
		sb.AppendFormat("Work per frame {0:0.0} ms (worst {1:0.0})  <- CPU cost of the game itself\n", workSum / n, workMax);
		sb.AppendFormat("Managed (C#) heap {0:0} MB", GC.GetTotalMemory(false) / 1048576.0);
		long unityAlloc = Profiler.GetTotalAllocatedMemoryLong();
		if (unityAlloc > 0)
		{
			sb.AppendFormat("   Unity allocated {0:0} MB", unityAlloc / 1048576.0);
		}
		sb.Append("\n");
		float heap = DevMenu_HeapMB();
		if (heap > 0f)
		{
			sb.AppendFormat("WebAssembly heap {0:0} MB (only ever grows)   JS heap {1:0} MB\n", heap, DevMenu_JSHeapMB());
		}
		sb.AppendFormat("Screen {0}x{1} fullscreen {2}   quality {3}   frame cap {4}\n", Screen.width, Screen.height, Screen.fullScreen, QualitySettings.names[QualitySettings.GetQualityLevel()], Application.targetFrameRate);
		sb.AppendFormat("Scene {0}   GPU {1}   system RAM {2} MB", SceneManager.GetActiveScene().name, SystemInfo.graphicsDeviceName, SystemInfo.systemMemorySize);
		statsText = sb.ToString();
	}

	// ============================================================================================
	// buttons
	// ============================================================================================
	public void CountObjects()
	{
		Renderer[] rs = FindObjectsOfType<Renderer>();
		int visibleCount = 0;
		for (int i = 0; i < rs.Length; i++)
		{
			if (rs[i].isVisible)
			{
				visibleCount++;
			}
		}
		Animator[] an = FindObjectsOfType<Animator>();
		int animOn = 0;
		for (int i = 0; i < an.Length; i++)
		{
			if (an[i].enabled)
			{
				animOn++;
			}
		}
		ParticleSystem[] ps = FindObjectsOfType<ParticleSystem>();
		int psPlaying = 0;
		for (int i = 0; i < ps.Length; i++)
		{
			if (ps[i].isPlaying)
			{
				psPlaying++;
			}
		}
		AudioSource[] au = FindObjectsOfType<AudioSource>();
		int auPlaying = 0;
		for (int i = 0; i < au.Length; i++)
		{
			if (au[i].isPlaying)
			{
				auPlaying++;
			}
		}
		Light[] li = FindObjectsOfType<Light>();
		countsText = string.Format("Active objects now: renderers {0} (on screen {1}), animators {2} (enabled {3}), particle systems {4} (playing {5}), audio sources {6} (playing {7}), lights {8}", rs.Length, visibleCount, an.Length, animOn, ps.Length, psPlaying, au.Length, auPlaying, li.Length);
	}

	public void CollectGarbage()
	{
		double before = GC.GetTotalMemory(false) / 1048576.0;
		double after = GC.GetTotalMemory(true) / 1048576.0;
		Resources.UnloadUnusedAssets();
		gcText = string.Format("C# heap {0:0} MB -> {1:0} MB. (The WebAssembly heap never shrinks, so Chrome's number will not drop.)", before, after);
	}

	public void ResetAll()
	{
		for (int i = options.Count - 1; i >= 0; i--)
		{
			SetOption(options[i], false);
		}
	}

	public void StartTest(Option o)
	{
		if (!testing)
		{
			StartCoroutine(RunTest(o));
		}
	}

	private IEnumerator Measure(float seconds)
	{
		sumDt = 0f;
		sumWork = 0f;
		sumFrames = 0;
		measuring = true;
		float end = Time.realtimeSinceStartup + seconds;
		while (Time.realtimeSinceStartup < end)
		{
			yield return null;
		}
		measuring = false;
		avgDt = sumFrames > 0 ? sumDt / sumFrames : 0f;
		avgWork = sumFrames > 0 ? sumWork / sumFrames : 0f;
	}

	private IEnumerator RunTest(Option o)
	{
		testing = true;
		bool original = o.on;
		o.result = "measuring (keep the game running and do not touch anything)...";
		yield return Measure(3f);
		float firstWork = avgWork;
		float firstDt = avgDt;
		SetOption(o, !original);
		o.result = "measuring the other state...";
		yield return new WaitForSecondsRealtime(0.8f);
		yield return Measure(3f);
		float secondWork = avgWork;
		float secondDt = avgDt;
		SetOption(o, original);

		float workOn = original ? firstWork : secondWork;
		float workOff = original ? secondWork : firstWork;
		float dtOn = original ? firstDt : secondDt;
		float dtOff = original ? secondDt : firstDt;
		float pct = workOff > 0f ? (workOn - workOff) / workOff * 100f : 0f;
		o.result = string.Format("work {0:0.0} ms with it ON vs {1:0.0} ms OFF ({2:+0;-0;0}%), frame {3:0.0} vs {4:0.0} ms", workOn, workOff, pct, dtOn * 1000f, dtOff * 1000f);
		testing = false;
	}

	// ============================================================================================
	// drawing (called by DevMenuGui, which is only enabled while the menu is open)
	// ============================================================================================
	private Vector2 scroll;
	private GUIStyle noteStyle;

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
		float w = Mathf.Min(560f, Screen.width / s - 20f);
		float h = Screen.height / s - 20f;
		GUILayout.BeginArea(new Rect(10f, 10f, w, h), GUI.skin.box);

		GUILayout.Label("DEV MENU  (press M to close)   Options reset when the page reloads.");
		GUILayout.Label(statsText);

		GUILayout.BeginHorizontal();
		GUILayout.Label("Quality level:", GUILayout.Width(90f));
		string[] names = QualitySettings.names;
		for (int i = 0; i < names.Length; i++)
		{
			if (GUILayout.Button(names[i]))
			{
				QualitySettings.SetQualityLevel(i, true);
			}
		}
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Reset all options"))
		{
			ResetAll();
		}
		if (GUILayout.Button("Count objects"))
		{
			CountObjects();
		}
		if (GUILayout.Button("Free C# memory"))
		{
			CollectGarbage();
		}
		GUILayout.EndHorizontal();
		if (countsText.Length > 0)
		{
			GUILayout.Label(countsText, noteStyle);
		}
		if (gcText.Length > 0)
		{
			GUILayout.Label(gcText, noteStyle);
		}

		GUILayout.BeginHorizontal();
		GUILayout.Label("Retro pixel size:", GUILayout.Width(110f));
		for (int f = 2; f <= 4; f++)
		{
			if (GUILayout.Button(f + "x" + (retroFactor == f ? "  <" : "")))
			{
				retroFactor = f;
				if (retroOption != null && retroOption.on)
				{
					ApplyRetro(true);
				}
			}
		}
		GUILayout.EndHorizontal();
		GUILayout.Label("Click an option's name to switch it ON (green) / OFF. It stays that way until you click again. 'Measure' runs a 6 second test, then puts the option back.", noteStyle);

		scroll = GUILayout.BeginScrollView(scroll);
		Color normalColor = GUI.backgroundColor;
		for (int i = 0; i < options.Count; i++)
		{
			Option o = options[i];
			GUILayout.BeginHorizontal();
			GUI.backgroundColor = o.on ? new Color(0.4f, 1f, 0.4f) : normalColor;
			if (GUILayout.Button((o.on ? "[ ON ]  " : "[ off ]  ") + o.name))
			{
				SetOption(o, !o.on);
			}
			GUI.backgroundColor = normalColor;
			GUI.enabled = !testing;
			if (GUILayout.Button("Measure", GUILayout.Width(70f)))
			{
				StartTest(o);
			}
			GUI.enabled = true;
			GUILayout.EndHorizontal();
			GUILayout.Label(o.note, noteStyle);
			if (!string.IsNullOrEmpty(o.result))
			{
				GUILayout.Label("=> " + o.result, noteStyle);
			}
		}
		GUILayout.EndScrollView();

		GUILayout.EndArea();
	}
}

// Separate component so the menu's OnGUI costs nothing while it is closed.
public class DevMenuGui : MonoBehaviour
{
	public DevMenu owner;

	private void OnGUI()
	{
		if (owner != null)
		{
			owner.Draw();
		}
	}
}
