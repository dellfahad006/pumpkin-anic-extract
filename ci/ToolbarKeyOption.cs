using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Option "Toolbar key: E". When ON (default), pressing E shows / hides the bottom toolbar
// (the key used to be Tab; Tab now pauses the game). When OFF, no key toggles the toolbar.
public static class ToolbarKeyOption
{
	private const string Key = "toolbarKeyE";

	public static bool Enabled
	{
		get { return PlayerPrefs.GetInt(Key, 1) == 1; }
		set
		{
			PlayerPrefs.SetInt(Key, value ? 1 : 0);
			PlayerPrefs.Save();
		}
	}
}

// Adds the option to the in-game options menu by copying the existing "Fullscreen" row,
// so it looks exactly like the other options. Runs on every scene load.
public class ToolbarKeyOptionInstaller : MonoBehaviour
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Create()
	{
		GameObject go = new GameObject("ToolbarKeyOptionInstaller");
		DontDestroyOnLoad(go);
		go.AddComponent<ToolbarKeyOptionInstaller>();
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
		StartCoroutine(Install());
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		StartCoroutine(Install());
	}

	private IEnumerator Install()
	{
		// let the scene (and fullscreenManager) finish starting up first
		yield return null;
		yield return null;

		Toggle fullscreenToggle = null;
		Toggle timerToggle = null;
		Toggle[] all = Resources.FindObjectsOfTypeAll<Toggle>();
		for (int i = 0; i < all.Length; i++)
		{
			if (!all[i].gameObject.scene.IsValid())
			{
				continue;
			}
			if (all[i].name == "FullScreenToggle")
			{
				fullscreenToggle = all[i];
			}
			else if (all[i].name == "Timer Toggle")
			{
				timerToggle = all[i];
			}
		}
		// the toolbar only exists in the game scenes, which are the ones that have the Timer option
		if (fullscreenToggle == null || timerToggle == null)
		{
			yield break;
		}
		Transform parent = fullscreenToggle.transform.parent;
		if (parent.Find("Toolbar Key Toggle") != null)
		{
			yield break;
		}

		GameObject toggleClone = Instantiate(fullscreenToggle.gameObject, parent);
		toggleClone.name = "Toolbar Key Toggle";
		// the copy must not keep the fullscreen click helper
		fullscreenPointerRelay relay = toggleClone.GetComponent<fullscreenPointerRelay>();
		if (relay != null)
		{
			Destroy(relay);
		}
		RectTransform toggleRect = toggleClone.GetComponent<RectTransform>();
		toggleRect.anchoredPosition = new Vector2(toggleRect.anchoredPosition.x, 290f);
		Toggle toggle = toggleClone.GetComponent<Toggle>();
		toggle.onValueChanged = new Toggle.ToggleEvent();
		toggle.interactable = true;
		toggle.SetIsOnWithoutNotify(ToolbarKeyOption.Enabled);
		toggle.onValueChanged.AddListener(delegate(bool value)
		{
			ToolbarKeyOption.Enabled = value;
		});

		Transform fullscreenText = parent.Find("FullScreenText");
		if (fullscreenText != null)
		{
			GameObject textClone = Instantiate(fullscreenText.gameObject, parent);
			textClone.name = "Toolbar Key Text";
			RectTransform textRect = textClone.GetComponent<RectTransform>();
			textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, 284f);
			TMP_Text tmp = textClone.GetComponent<TMP_Text>();
			if (tmp != null)
			{
				tmp.text = "Toolbar key (E)";
			}
			else
			{
				Text ui = textClone.GetComponent<Text>();
				if (ui != null)
				{
					ui.text = "Toolbar key (E)";
				}
			}
		}
	}
}
