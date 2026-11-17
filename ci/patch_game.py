#!/usr/bin/env python3
"""
Patches applied to the extracted Unity project (./project) right before the WebGL build.
Every patch asserts that it found what it expected, so the build fails loudly instead of
silently skipping a change. Safe to run more than once.
"""
import glob
import os
import re
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else 'project'
A = ROOT + '/Assets'
S = A + '/Scripts/Assembly-CSharp'
P = ROOT + '/ProjectSettings'


def read(path):
    with open(path, encoding='utf-8', newline='') as f:
        return f.read()


def write(path, text):
    with open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(text)


def sub_once(text, old, new, what):
    assert text.count(old) == 1, '%s: expected exactly 1 match, found %d' % (what, text.count(old))
    return text.replace(old, new)


# ---------- Unity YAML helpers (text based, keeps the file byte-for-byte otherwise) ----------
HDR = re.compile(r'(?m)^(--- !u!(\d+) &(\d+)[^\n]*\n)')


def split_docs(text):
    parts = HDR.split(text)
    head = parts[0]
    docs = []  # [classId, fileId, header, body]
    for i in range(1, len(parts), 4):
        docs.append([parts[i + 1], parts[i + 2], parts[i], parts[i + 3]])
    return head, docs


def join_docs(head, docs):
    return head + ''.join(d[2] + d[3] for d in docs)


def go_name(body):
    m = re.search(r'\n  m_Name: ([^\n]*)', body)
    return m.group(1).strip() if m else ''


def components_of(go_body):
    return re.findall(r'component: \{fileID: (\d+)\}', go_body)


def find_gos(docs, name):
    return [d for d in docs if d[0] == '1' and go_name(d[3]) == name]


def doc_by_id(docs, fid):
    for d in docs:
        if d[1] == fid:
            return d
    return None


def set_field(body, key, value, what):
    new, n = re.subn(r'(\n  %s: )[^\n]*' % re.escape(key), r'\g<1>%s' % value, body, count=1)
    assert n == 1, what + ': field %s not found' % key
    return new


# =====================================================================================
# 1. Gate: hide the two chains once the ChainOpen animation has finished
#    (the clip only moves the bones, it never hides the sprites, so an "X" was left behind)
# =====================================================================================
p = S + '/openDoor.cs'
s = read(p)
if 'hideChains' not in s:
    s = sub_once(s, '\t\t\tStartCoroutine(borraOutline());\n',
                 '\t\t\tStartCoroutine(borraOutline());\n\t\t\tStartCoroutine(hideChains());\n', 'openDoor call')
    helper = (
        '\tprivate IEnumerator hideChains()\n'
        '\t{\n'
        '\t\t// ChainOpen lasts ~2.83s; hide the leftover chain sprites right after it ends\n'
        '\t\tyield return new WaitForSeconds(2.9f);\n'
        '\t\tif (chains1 != null)\n'
        '\t\t{\n'
        '\t\t\tchains1.gameObject.SetActive(value: false);\n'
        '\t\t}\n'
        '\t\tif (chains2 != null)\n'
        '\t\t{\n'
        '\t\t\tchains2.gameObject.SetActive(value: false);\n'
        '\t\t}\n'
        '\t}\n\n')
    s = sub_once(s, '\tprivate IEnumerator startCooldown()', helper + '\tprivate IEnumerator startCooldown()', 'openDoor helper')
    write(p, s)
    print('openDoor.cs: chains now hidden after the gate opens')

# =====================================================================================
# 2. Wardrobe becomes decoration: switch off its trigger collider.
#    All interaction (outline, "press F", opening the UI) only starts from that trigger,
#    so nothing can open it. The script stays so pauseGame / fishScreamer keep working.
# =====================================================================================
for scene in ('Nightmare', 'SampleScene'):
    path = A + '/Scenes/%s.unity' % scene
    head, docs = split_docs(read(path))
    found = 0
    for go in find_gos(docs, 'Closet'):
        for cid in components_of(go[3]):
            c = doc_by_id(docs, cid)
            if c and c[0] == '65':  # BoxCollider
                c[3] = set_field(c[3], 'm_Enabled', '0', scene + ' Closet collider')
                found += 1
    assert found == 1, '%s: expected 1 wardrobe collider, found %d' % (scene, found)
    write(path, join_docs(head, docs))
    print(scene + ': wardrobe trigger collider disabled (decoration only)')

# =====================================================================================
# 3. Main menu: remove the music + its mute icon, and make the logo static
# =====================================================================================
path = A + '/Scenes/MainMenu.unity'
text = read(path)
clip = 'm_audioClip: {fileID: 8300000, guid: 063517ce8abd2bc4f949dd17c7e9d761, type: 3}'  # CrazyAccordionsF.ogg
if clip in text:
    text = sub_once(text, clip, 'm_audioClip: {fileID: 0}', 'menu music clip')
head, docs = split_docs(text)
mute = find_gos(docs, 'Mute Music')
assert len(mute) == 1, 'Mute Music object not found'
mute[0][3] = set_field(mute[0][3], 'm_IsActive', '0', 'Mute Music')
tit = find_gos(docs, 'Tittle')
assert len(tit) == 1, 'Tittle object not found'
n = 0
for cid in components_of(tit[0][3]):
    c = doc_by_id(docs, cid)
    if c and c[0] == '95':  # Animator
        c[3] = set_field(c[3], 'm_Enabled', '0', 'Tittle animator')
        n += 1
assert n == 1, 'Tittle animator not found'
write(path, join_docs(head, docs))
print('MainMenu: music removed, mute icon hidden, logo animator disabled')

# =====================================================================================
# 4. Keyboard: P also pauses / skips the tutorial / closes the shop (Esc still works).
#    In fullscreen the browser swallows Esc to leave fullscreen, so Esc can never work there.
#    (Tab is already the Hotbar key, so it is not used.)
# =====================================================================================
path = P + '/InputManager.asset'
text = read(path)
pat = re.compile(r'(    m_Name: Start\n(?:(?!    m_Name:).*\n)*?    positiveButton: escape\n    altNegativeButton:\n    altPositiveButton:)(\n)')
if not re.search(r'positiveButton: escape\n    altNegativeButton:\n    altPositiveButton: p\n', text):
    text, n = pat.subn(r'\1 p\2', text, count=1)
    assert n == 1, 'Start axis not found in InputManager'
    write(path, text)
for scene in ('Nightmare', 'SampleScene'):
    path = A + '/Scenes/%s.unity' % scene
    text = read(path)
    if 'SKIP TUTORIAL [ESC]' in text:
        write(path, text.replace('SKIP TUTORIAL [ESC]', 'SKIP TUTORIAL [ESC / P]'))
print('Input: P added as a second Start/pause key; tutorial hint updated')

# =====================================================================================
# 5. Fullscreen (one click) + working resolution dropdown
# =====================================================================================
FULLSCREEN = r'''using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Fullscreen toggle + resolution dropdown, rewritten for WebGL.
//
// Why the toggle needed two clicks: browsers only allow "enter fullscreen" inside a real
// mouse event, and Unity WebGL performs the request on the next mouse-up. A UI Toggle fires
// on mouse-up, which is too late, so the first click only armed it. We now ask for fullscreen
// on pointer-DOWN, so the same click's mouse-up completes it. Leaving fullscreen never needed this.
public class fullscreenManager : MonoBehaviour
{
	public Toggle toggle;

	public TMP_Dropdown resolucionesDropDown;

	private readonly List<Vector2Int> sizes = new List<Vector2Int>();

	private float lastPointerToggleTime = -10f;

	private static readonly Vector2Int[] presets = new Vector2Int[5]
	{
		new Vector2Int(960, 540),
		new Vector2Int(1280, 720),
		new Vector2Int(1600, 900),
		new Vector2Int(1920, 1080),
		new Vector2Int(2560, 1440)
	};

	private bool InPointerWindow
	{
		get { return Time.unscaledTime - lastPointerToggleTime < 1f; }
	}

	private void Start()
	{
		if (toggle != null)
		{
			toggle.SetIsOnWithoutNotify(Screen.fullScreen);
			if (toggle.GetComponent<fullscreenPointerRelay>() == null)
			{
				toggle.gameObject.AddComponent<fullscreenPointerRelay>().owner = this;
			}
		}
		RevisarResolucion();
	}

	private void Update()
	{
		// Keep the toggle in sync with the real state (the browser can leave fullscreen with Esc),
		// but not during the moment right after a click, while the browser is still switching.
		if (toggle != null && !InPointerWindow && toggle.isOn != Screen.fullScreen)
		{
			toggle.SetIsOnWithoutNotify(Screen.fullScreen);
		}
	}

	// Called on pointer-down over the toggle (see fullscreenPointerRelay).
	public void OnTogglePointerDown()
	{
		if (toggle == null || !toggle.interactable)
		{
			return;
		}
		lastPointerToggleTime = Time.unscaledTime;
		Screen.fullScreen = !Screen.fullScreen;
	}

	// Toggle's On Value Changed event (also used for keyboard / gamepad activation).
	public void ActivarPantallaCompleta(bool pantallaCompleta)
	{
		if (InPointerWindow)
		{
			return; // already handled on pointer-down
		}
		if (Screen.fullScreen != pantallaCompleta)
		{
			Screen.fullScreen = pantallaCompleta;
		}
	}

	public void RevisarResolucion()
	{
		sizes.Clear();
		List<string> options = new List<string>();

		// Real display modes if the platform reports several; WebGL normally reports none,
		// so offer common sizes up to the size of the player's screen instead.
		Resolution[] modes = Screen.resolutions;
		if (modes != null)
		{
			for (int i = 0; i < modes.Length; i++)
			{
				Vector2Int v = new Vector2Int(modes[i].width, modes[i].height);
				if (!sizes.Contains(v))
				{
					sizes.Add(v);
				}
			}
		}
		if (sizes.Count < 2)
		{
			sizes.Clear();
			Resolution native = Screen.currentResolution;
			int maxW = Mathf.Max(native.width, Screen.width);
			int maxH = Mathf.Max(native.height, Screen.height);
			for (int j = 0; j < presets.Length; j++)
			{
				if (presets[j].x <= maxW && presets[j].y <= maxH)
				{
					sizes.Add(presets[j]);
				}
			}
			Vector2Int nativeSize = new Vector2Int(maxW, maxH);
			if (!sizes.Contains(nativeSize))
			{
				sizes.Add(nativeSize);
			}
		}
		sizes.Sort((Vector2Int a, Vector2Int b) => (a.x * a.y).CompareTo(b.x * b.y));
		for (int k = 0; k < sizes.Count; k++)
		{
			options.Add(sizes[k].x + " x " + sizes[k].y);
		}

		// Pick the saved size if there is one, otherwise the entry closest to the current window.
		int selected = -1;
		if (PlayerPrefs.HasKey("resW") && PlayerPrefs.HasKey("resH"))
		{
			selected = sizes.IndexOf(new Vector2Int(PlayerPrefs.GetInt("resW"), PlayerPrefs.GetInt("resH")));
		}
		bool fromSave = selected >= 0;
		if (selected < 0)
		{
			int best = int.MaxValue;
			for (int m = 0; m < sizes.Count; m++)
			{
				int diff = Mathf.Abs(sizes[m].x - Screen.width) + Mathf.Abs(sizes[m].y - Screen.height);
				if (diff < best)
				{
					best = diff;
					selected = m;
				}
			}
		}

		resolucionesDropDown.ClearOptions();
		resolucionesDropDown.AddOptions(options);
		resolucionesDropDown.SetValueWithoutNotify(selected);
		resolucionesDropDown.RefreshShownValue();
		resolucionesDropDown.interactable = sizes.Count > 1;

		if (fromSave && (sizes[selected].x != Screen.width || sizes[selected].y != Screen.height))
		{
			Screen.SetResolution(sizes[selected].x, sizes[selected].y, Screen.fullScreen);
		}
	}

	// Dropdown's On Value Changed event.
	public void CambiarResolucion(int indice)
	{
		if (indice < 0 || indice >= sizes.Count)
		{
			return;
		}
		PlayerPrefs.SetInt("resW", sizes[indice].x);
		PlayerPrefs.SetInt("resH", sizes[indice].y);
		PlayerPrefs.Save();
		Screen.SetResolution(sizes[indice].x, sizes[indice].y, Screen.fullScreen);
	}
}

// Small helper added to the toggle at runtime: forwards pointer-down to fullscreenManager.
public class fullscreenPointerRelay : MonoBehaviour, IPointerDownHandler, IEventSystemHandler
{
	public fullscreenManager owner;

	public void OnPointerDown(PointerEventData eventData)
	{
		if (owner != null)
		{
			owner.OnTogglePointerDown();
		}
	}
}
'''
write(S + '/fullscreenManager.cs', FULLSCREEN.replace('\r\n', '\n'))
print('fullscreenManager.cs rewritten (one-click fullscreen, working resolution dropdown)')

# =====================================================================================
# 6. Performance
# =====================================================================================
# 6a. WebGL: setting Application.targetFrameRate makes Unity drive the loop with a timer
#     instead of the browser's requestAnimationFrame, which is smoother and cheaper. Leave it alone.
p = S + '/FrameRate.cs'
s = read(p)
if 'UNITY_WEBGL' not in s:
    s = sub_once(s, '\t\tApplication.targetFrameRate = Screen.currentResolution.refreshRate;\n',
                 '#if !UNITY_WEBGL || UNITY_EDITOR\n\t\tApplication.targetFrameRate = Screen.currentResolution.refreshRate;\n#endif\n', 'FrameRate')
    write(p, s)

# 6b. New players start on Medium instead of High (saved choices are kept).
p = S + '/GraphicSettings.cs'
s = read(p)
if 'GetInt("graphicsQuality", 1)' not in s:
    s = sub_once(s, 'GetInt("graphicsQuality", 2)', 'GetInt("graphicsQuality", 1)', 'GraphicSettings default')
    write(p, s)

# 6c. Make the Low / Medium quality levels actually cheaper (shadows, lights).
p = P + '/QualitySettings.asset'
text = read(p)


def set_in_block(text, level, key, value):
    start = text.index('    name: %s\n' % level)
    nxt = text.find('\n  - serializedVersion', start)
    end = nxt if nxt != -1 else len(text)
    block = text[start:end]
    new, n = re.subn(r'(\n    %s: )[^\n]*' % re.escape(key), r'\g<1>%s' % value, block, count=1)
    assert n == 1, 'QualitySettings %s.%s not found' % (level, key)
    return text[:start] + new + text[end:]


for level, key, value in (('Low', 'shadows', 0), ('Low', 'pixelLightCount', 2), ('Low', 'skinWeights', 2),
                          ('Medium', 'shadows', 1), ('Medium', 'shadowResolution', 0),
                          ('Medium', 'shadowDistance', 25), ('Medium', 'pixelLightCount', 4)):
    text = set_in_block(text, level, key, value)
write(p, text)

# 6d. Cameras render in HDR, but the workflow turns every post-processing layer off, so nothing ever
#     uses the HDR data. It only costs an extra half-float render target and a full-screen copy.
#     (If you ever re-enable post-processing / bloom, set this back to 1.)
for scene in ('Nightmare', 'SampleScene', 'MainMenu', 'EndGame'):
    path = A + '/Scenes/%s.unity' % scene
    text = read(path)
    if '\n  m_HDR: 1\n' in text:
        text, n = re.subn(r'(\n  m_HDR: )1(\n)', r'\g<1>0\2', text)
        assert n >= 1
        write(path, text)

# 6e. Animators that only wiggle sprites (grass, crop plots, water foam) keep updating transforms even
#     while completely off-screen. "Cull Update Transforms" (1) skips that work only while none of their
#     renderers is visible; their state machines keep running, so game logic is unaffected.
#     Only animators that really have a renderer in their hierarchy are changed.
CULL_CONTROLLERS = ('Grass2', 'Plant1CropableDirt', 'Plant2CropableDirt', 'Plant3CropableDirt',
                    'Plant4CropableDirt', 'Plant5CropableDirt', 'waterEspuma1')
guid_to_ctrl = {}
for mf in glob.glob(A + '/AnimatorController/*.controller.meta'):
    g = re.search(r'guid: (\w+)', read(mf))
    if g:
        guid_to_ctrl[g.group(1)] = os.path.basename(mf)[:-len('.controller.meta')]
for scene in ('Nightmare', 'SampleScene'):
    path = A + '/Scenes/%s.unity' % scene
    head, docs = split_docs(read(path))
    by_id = {d[1]: d for d in docs}
    children = {}  # GameObject id -> child GameObject ids
    go_of_tr = {}
    for d in docs:
        if d[0] in ('4', '224'):
            go_of_tr[d[1]] = re.search(r'm_GameObject: \{fileID: (\d+)', d[3]).group(1)
    for d in docs:
        if d[0] in ('4', '224'):
            go = go_of_tr[d[1]]
            father = re.search(r'm_Father: \{fileID: (\d+)', d[3]).group(1)
            if father in go_of_tr:
                children.setdefault(go_of_tr[father], []).append(go)

    def has_renderer(go_id):
        stack = [go_id]
        while stack:
            g = stack.pop()
            for cid in components_of(by_id[g][3]):
                if cid in by_id and by_id[cid][0] in ('212', '23'):
                    return True
            stack.extend(children.get(g, []))
        return False

    changed = 0
    for d in docs:
        if d[0] != '95':
            continue
        ctl = re.search(r'm_Controller: \{[^}]*guid: (\w+)', d[3])
        if not ctl or guid_to_ctrl.get(ctl.group(1)) not in CULL_CONTROLLERS:
            continue
        go = re.search(r'm_GameObject: \{fileID: (\d+)', d[3]).group(1)
        if go in by_id and has_renderer(go):
            d[3] = set_field(d[3], 'm_CullingMode', '1', 'animator culling')
            changed += 1
    assert changed > 0, scene + ': no animators matched'
    write(path, join_docs(head, docs))
    print('%s: %d visual-only animators now skip work while off-screen' % (scene, changed))

# 6f. Bottom-bar counters rewrote 8 text labels EVERY frame, allocating a new string each time. In WebGL the
#     garbage collector cannot run incrementally, so that churn shows up as small stutters. Now a label is
#     only touched when its number actually changes, and the component lookup is cached.
for fname in ('barIcons.cs', 'barIcons2.cs'):
    p = S + '/' + fname
    s = read(p)
    if 'SetCount(' in s:
        continue
    pat = re.compile(r'(\t\t)(\w+)\.text = gameManager\.GetComponent<(\w+)>\(\)\.(\w+)\.ToString\(\) \?\? "";\n')
    found = pat.findall(s)
    assert len(found) == 8, '%s: expected 8 counter labels, found %d' % (fname, len(found))
    comp = found[0][2]
    assert all(f[2] == comp for f in found)
    src = read(S + '/%s.cs' % comp)
    for f in found:
        assert re.search(r'public int [^;]*\b%s\b' % f[3], src), '%s.%s is not an int' % (comp, f[3])
    s = pat.sub(lambda m: '%sSetCount(%s, ref shown_%s, resourceSource.%s);\n' % (m.group(1), m.group(2), m.group(4), m.group(4)), s)
    s = s.replace('gameManager.GetComponent<%s>()' % comp, 'resourceSource')
    members = ''.join('\tprivate int shown_%s = -1;\n' % f[3] for f in found)
    members += (
        '\n\tprivate %(c)s cachedSource;\n\n'
        '\tprivate %(c)s resourceSource\n\t{\n\t\tget\n\t\t{\n'
        '\t\t\tif (cachedSource == null)\n\t\t\t{\n'
        '\t\t\t\tcachedSource = gameManager.GetComponent<%(c)s>();\n\t\t\t}\n'
        '\t\t\treturn cachedSource;\n\t\t}\n\t}\n\n'
        '\tprivate static void SetCount(TMP_Text label, ref int shown, int value)\n\t{\n'
        '\t\tif (shown != value)\n\t\t{\n\t\t\tshown = value;\n\t\t\tlabel.text = value.ToString();\n\t\t}\n\t}\n\n') % {'c': comp}
    s = sub_once(s, '\tprivate void Update()', members + '\tprivate void Update()', fname + ' members')
    write(p, s)
    print(fname + ': counters only update when the number changes')

# 6g. Log/Warning calls capture a stack trace each time; players never see it. (Errors keep theirs.)
p = P + '/ProjectSettings.asset'
text = read(p)
OLD_ST = '  m_StackTraceTypes: 010000000100000001000000010000000100000001000000\n'
NEW_ST = '  m_StackTraceTypes: 010000000100000000000000000000000100000001000000\n'  # Warning + Log = none
if OLD_ST in text:
    text = text.replace(OLD_ST, NEW_ST)
else:
    assert NEW_ST in text, 'StackTraceTypes not found'
# 6h. Cache the big game data in the browser (IndexedDB) so repeat visits start faster.
text = re.sub(r'(\n  webGLDataCaching: )0(\n)', r'\g<1>1\2', text)
write(p, text)
print('Performance patches applied')
# =====================================================================================
# 7. Hidden dev menu (press M). Source files live next to this script in ci/.
# =====================================================================================
import shutil
HERE = os.path.dirname(os.path.abspath(__file__))
os.makedirs(A + '/Plugins/WebGL', exist_ok=True)
shutil.copy(HERE + '/DevMenu.cs', S + '/DevMenu.cs')
shutil.copy(HERE + '/DevMenu.jslib', A + '/Plugins/WebGL/DevMenu.jslib')
print('Dev menu installed (press M in the game)')
print('All patches applied.')
