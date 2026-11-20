using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Hidden developer cheat menu. Press L to open / close it. (M = performance menu, N = Fix Lab.)
// Nothing is created on screen and nothing runs per frame until a toggle is switched on.
// Everything here only touches fields that exist in the game's own scripts; if something is missing in a
// scene (e.g. no deer in the main menu) the message line at the bottom says so instead of failing.
public class CheatMenu : MonoBehaviour
{
	private const bool Enabled = true; // set to false to remove the menu from a build

	private static CheatMenu instance;

	private bool visible;
	private CheatMenuGui gui;
	private string message = "Cheat menu ready.";
	private bool god;
	private bool infStamina;
	private bool infWater;
	private float tick;
	private FieldInfo soundedField;
	private GUIStyle small;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		if (!Enabled || instance != null)
		{
			return;
		}
		GameObject go = new GameObject("CheatMenu");
		DontDestroyOnLoad(go);
		instance = go.AddComponent<CheatMenu>();
	}

	private void Awake()
	{
		gui = gameObject.AddComponent<CheatMenuGui>();
		gui.owner = this;
		gui.enabled = false;
		soundedField = typeof(enemyController).GetField("soundedAlready", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.L))
		{
			visible = !visible;
			gui.enabled = visible;
		}
		if (infStamina)
		{
			playerController p = FindObjectOfType<playerController>();
			if (p != null)
			{
				p.stamina = p.maxStamina;
			}
		}
		if (infWater)
		{
			moneyCount m = FindObjectOfType<moneyCount>();
			if (m != null)
			{
				m.water = m.maxWater;
			}
		}
		if (god)
		{
			tick += Time.unscaledDeltaTime;
			if (tick >= 0.2f)
			{
				tick = 0f;
				ApplyGod();
			}
		}
	}

	// Enemies can only hurt you through "attack once" flags; keep those switched on.
	private void ApplyGod()
	{
		enemyController[] ghouls = FindObjectsOfType<enemyController>();
		for (int i = 0; i < ghouls.Length; i++)
		{
			if (soundedField != null)
			{
				soundedField.SetValue(ghouls[i], true);
			}
		}
		wendigoController[] deer = FindObjectsOfType<wendigoController>();
		for (int i = 0; i < deer.Length; i++)
		{
			deer[i].alreadyAttack = true;
		}
		albinoWendigoController[] albino = FindObjectsOfType<albinoWendigoController>();
		for (int i = 0; i < albino.Length; i++)
		{
			albino[i].alreadyAttack = true;
		}
	}

	// ---------------------------------------------------------------------------------------------
	// actions
	// ---------------------------------------------------------------------------------------------
	private static T FindAny<T>() where T : Component
	{
		T[] all = Resources.FindObjectsOfTypeAll<T>();
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].gameObject.scene.IsValid())
			{
				return all[i];
			}
		}
		return null;
	}

	private void AddCoins()
	{
		moneyCount m = FindObjectOfType<moneyCount>();
		if (m == null)
		{
			message = "No money counter in this scene.";
			return;
		}
		m.count += 100;
		m.totalMoney += 100;
		message = "+100 coins (now " + m.count + ").";
	}

	private void AddItems()
	{
		moneyCount m = FindObjectOfType<moneyCount>();
		if (m == null)
		{
			message = "No inventory in this scene.";
			return;
		}
		m.numSeeds1 += 10;
		m.numSeeds2 += 10;
		m.numSeeds3 += 10;
		m.numSeeds4 += 10;
		m.numSeeds5 += 10;
		m.numStick += 10;
		m.numFiber += 10;
		m.numIrisCoin += 10;
		message = "+10 of every seed, stick, fiber and iris coin.";
	}

	private void RefillWater()
	{
		moneyCount m = FindObjectOfType<moneyCount>();
		if (m == null)
		{
			message = "No water bar in this scene.";
			return;
		}
		m.water = m.maxWater;
		m.alreadyOut = false;
		if (m.waterEdge != null)
		{
			m.waterEdge.SetActive(true);
		}
		message = "Water refilled.";
	}

	// The game's own goNight() is NOT "night now": it restarts the 480 s sun-dimming clip from the beginning and waits
	// 470 s. So night / day are driven here by calling the cycle's real steps directly (works for lightCycle and lightCycle_2).
	private MonoBehaviour FindCycle()
	{
		MonoBehaviour c = FindAny<lightCycle>();
		if (c == null)
		{
			c = FindAny<lightCycle_2>();
		}
		return c;
	}

	private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private void GoNight()
	{
		MonoBehaviour c = FindCycle();
		if (c == null)
		{
			message = "No day/night cycle in this scene.";
			return;
		}
		Type t = c.GetType();
		MethodInfo fade = t.GetMethod("AmbientToNight", Any);
		FieldInfo sunField = t.GetField("sun", Any);
		if (fade == null || sunField == null)
		{
			message = "Day/night script has no AmbientToNight / sun.";
			return;
		}
		c.StopAllCoroutines();
		Animator sun = sunField.GetValue(c) as Animator;
		if (sun != null)
		{
			sun.Play("sunGoNight", 0, 0.995f); // jump to the end of the 480 s dimming clip
		}
		c.StartCoroutine((IEnumerator)fade.Invoke(c, null)); // ambient fades to night (6 s), sets isNight, starts the night timer
		message = "Night: sun clip jumped to its end, ambient fading to night.";
	}

	private void GoDay()
	{
		MonoBehaviour c = FindCycle();
		if (c == null)
		{
			message = "No day/night cycle in this scene.";
			return;
		}
		Type t = c.GetType();
		MethodInfo day = t.GetMethod("goDay", Any);
		if (day == null)
		{
			message = "goDay not found.";
			return;
		}
		c.StopAllCoroutines();
		FieldInfo nightField = t.GetField("isNight", Any);
		if (nightField != null)
		{
			nightField.SetValue(c, false);
		}
		day.Invoke(c, null); // ambient back to day, sun brightens (10 s), then the normal day -> night timer restarts
		message = "Day: ambient + sun back to day, normal day/night timer restarted.";
	}

	private Vector3 NearPlayer(float dx)
	{
		playerController p = FindObjectOfType<playerController>();
		return p != null ? p.transform.position + new Vector3(dx, 0f, 0f) : Vector3.zero;
	}

	private static void Place(GameObject go, Vector3 pos)
	{
		NavMeshAgent a = go.GetComponent<NavMeshAgent>();
		if (a != null && a.isOnNavMesh)
		{
			a.Warp(pos);
		}
		else
		{
			go.transform.position = pos;
		}
	}

	private void SpawnGhoul()
	{
		enemyController e = FindAny<enemyController>();
		if (e == null)
		{
			message = "No monster in this scene.";
			return;
		}
		e.gameObject.SetActive(true);
		Place(e.gameObject, NearPlayer(9f));
		e.followPlayer = true;
		e.goHome = false;
		message = "Monster placed next to you (chasing).";
	}

	private void SpawnDeer()
	{
		wendigoController d = FindAny<wendigoController>();
		if (d == null)
		{
			message = "No deer in this scene.";
			return;
		}
		d.gameObject.SetActive(true);
		Place(d.gameObject, NearPlayer(7f));
		message = "Deer placed next to you (walk close to it to make it transform).";
	}

	private void SpawnAlbino()
	{
		albinoWendigoController d = FindAny<albinoWendigoController>();
		if (d == null)
		{
			message = "No albino deer in this scene.";
			return;
		}
		d.gameObject.SetActive(true);
		Place(d.gameObject, NearPlayer(7f));
		message = "Albino deer placed next to you.";
	}

	private void SendAway()
	{
		enemyController[] ghouls = Resources.FindObjectsOfTypeAll<enemyController>();
		for (int i = 0; i < ghouls.Length; i++)
		{
			if (ghouls[i].gameObject.scene.IsValid() && ghouls[i].gameObject.activeInHierarchy)
			{
				ghouls[i].followPlayer = false;
				ghouls[i].goHome = true;
			}
		}
		wendigoController[] deer = Resources.FindObjectsOfTypeAll<wendigoController>();
		for (int i = 0; i < deer.Length; i++)
		{
			if (deer[i].gameObject.scene.IsValid())
			{
				deer[i].followPlayer = false;
			}
		}
		albinoWendigoController[] albino = Resources.FindObjectsOfTypeAll<albinoWendigoController>();
		for (int i = 0; i < albino.Length; i++)
		{
			if (albino[i].gameObject.scene.IsValid())
			{
				albino[i].followPlayer = false;
			}
		}
		message = "Enemies stopped chasing / heading home.";
	}

	private void Load(string scene)
	{
		Time.timeScale = 1f;
		SceneManager.LoadScene(scene);
	}

	// ---------------------------------------------------------------------------------------------
	// drawing
	// ---------------------------------------------------------------------------------------------
	private bool Toggle(bool value, string label)
	{
		Color old = GUI.backgroundColor;
		GUI.backgroundColor = value ? new Color(0.4f, 1f, 0.4f) : Color.white;
		bool pressed = GUILayout.Button((value ? "[ON]  " : "[off] ") + label, GUILayout.Height(30f));
		GUI.backgroundColor = old;
		return pressed ? !value : value;
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
		float w = Mathf.Min(360f, Screen.width / s - 20f);
		float h = Mathf.Min(640f, Screen.height / s - 20f);
		GUILayout.BeginArea(new Rect(Screen.width / s - w - 10f, 10f, w, h), GUI.skin.box);
		GUILayout.Label("CHEATS  (L closes)  Scene: " + SceneManager.GetActiveScene().name);

		god = Toggle(god, "God mode (monsters / deer cannot kill)");
		infStamina = Toggle(infStamina, "Infinite stamina");
		infWater = Toggle(infWater, "Infinite water");

		GUILayout.Label("Resources", small);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("+100 coins", GUILayout.Height(30f))) { AddCoins(); }
		if (GUILayout.Button("+10 all items", GUILayout.Height(30f))) { AddItems(); }
		if (GUILayout.Button("Refill water", GUILayout.Height(30f))) { RefillWater(); }
		GUILayout.EndHorizontal();

		GUILayout.Label("Time of day", small);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Go night now", GUILayout.Height(30f))) { GoNight(); }
		if (GUILayout.Button("Go day now", GUILayout.Height(30f))) { GoDay(); }
		GUILayout.EndHorizontal();

		GUILayout.Label("Enemies (bring them to you)", small);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Monster here", GUILayout.Height(30f))) { SpawnGhoul(); }
		if (GUILayout.Button("Deer here", GUILayout.Height(30f))) { SpawnDeer(); }
		if (GUILayout.Button("Albino here", GUILayout.Height(30f))) { SpawnAlbino(); }
		GUILayout.EndHorizontal();
		if (GUILayout.Button("Make enemies stop chasing", GUILayout.Height(28f))) { SendAway(); }

		GUILayout.Label("Game speed (skip waiting)", small);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("1x", GUILayout.Height(30f))) { Time.timeScale = 1f; message = "Speed 1x"; }
		if (GUILayout.Button("2x", GUILayout.Height(30f))) { Time.timeScale = 2f; message = "Speed 2x"; }
		if (GUILayout.Button("4x", GUILayout.Height(30f))) { Time.timeScale = 4f; message = "Speed 4x"; }
		if (GUILayout.Button("8x", GUILayout.Height(30f))) { Time.timeScale = 8f; message = "Speed 8x"; }
		GUILayout.EndHorizontal();

		GUILayout.Label("Scenes", small);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Reload", GUILayout.Height(28f))) { Load(SceneManager.GetActiveScene().name); }
		if (GUILayout.Button("Menu", GUILayout.Height(28f))) { Load("MainMenu"); }
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Normal", GUILayout.Height(28f))) { Load("SampleScene"); }
		if (GUILayout.Button("Nightmare", GUILayout.Height(28f))) { Load("Nightmare"); }
		if (GUILayout.Button("Ending", GUILayout.Height(28f))) { Load("EndGame"); }
		GUILayout.EndHorizontal();

		GUILayout.Label(message, small);
		GUILayout.EndArea();
	}
}

// Separate component so the menu's OnGUI costs nothing while it is closed.
public class CheatMenuGui : MonoBehaviour
{
	public CheatMenu owner;

	private void OnGUI()
	{
		if (owner != null)
		{
			owner.Draw();
		}
	}
}
