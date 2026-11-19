using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

// Replacement for the 2D Animation package's "SpriteSkin" component.
//
// WHY: in the WebGL build the original SpriteSkin objects do not exist at runtime (the Fix Lab found 0 of them
// while the player and the deer were on screen, and the console says "referenced script on PlayerTop is
// missing"). So hood, monster arms/legs and deer bodies were never deformed: they stayed in their rest pose while
// the bones (and everything plain that hangs on them, like heads and faces) moved.
//
// HOW: patch_game.py points every scene's SpriteSkin at THIS script instead. The two serialized fields below
// have the same names as the original ones, so the rig data (m_RootBone, m_BoneTransforms) is picked up as is.
// Every frame (LateUpdate, after the Animator) this script skins the sprite's mesh on the CPU with plain
// Unity sprite APIs and draws it through a child MeshRenderer; the original SpriteRenderer is only hidden
// (forceRenderingOff), never disabled, so animations that change its sprite / colour / sorting keep working.
//
// If anything about a sprite is unexpected (no bones, bone count mismatch), that one sprite is simply drawn the
// normal way again. Fix Lab (key N) can switch the whole thing on / off while you look at a character.
[DisallowMultipleComponent]
public class SoftSkin : MonoBehaviour
{
	// Switched by Fix Lab. false = back to the original (undeformed) drawing.
	public static bool Active = true;

	public static readonly List<SoftSkin> All = new List<SoftSkin>();

	[SerializeField] private Transform m_RootBone;
	[SerializeField] private Transform[] m_BoneTransforms;

	private SpriteRenderer sr;
	private Sprite builtFor;
	private bool built;
	private bool drawnOnce;
	private string status = "not built yet";
	private GameObject meshGo;
	private MeshRenderer mr;
	private MeshFilter mf;
	private Mesh mesh;
	private MaterialPropertyBlock block;
	private Vector3[] src;
	private Vector3[] dst;
	private BoneWeight[] weights;
	private Matrix4x4[] bind;
	private Matrix4x4[] palette;
	private Color shownColor = new Color(-1f, -1f, -1f, -1f);
	private string colorProp = "_Color";
	private int frame;

	public string Status
	{
		get { return status; }
	}

	public int Bones
	{
		get { return m_BoneTransforms != null ? m_BoneTransforms.Length : 0; }
	}

	public bool Drawing
	{
		get { return built && mr != null && mr.enabled; }
	}

	public bool Visible
	{
		get { return mr != null && mr.isVisible; }
	}

	private void OnEnable()
	{
		sr = GetComponent<SpriteRenderer>();
		All.Add(this);
		builtFor = null;
	}

	private void OnDisable()
	{
		All.Remove(this);
		Hide();
	}

	private void OnDestroy()
	{
		if (mesh != null)
		{
			Destroy(mesh);
		}
		if (meshGo != null)
		{
			Destroy(meshGo);
		}
		if (sr != null)
		{
			sr.forceRenderingOff = false;
		}
	}

	private void Hide()
	{
		if (mr != null)
		{
			mr.enabled = false;
		}
		if (sr != null)
		{
			sr.forceRenderingOff = false;
		}
	}

	private void LateUpdate()
	{
		if (sr == null)
		{
			sr = GetComponent<SpriteRenderer>();
			if (sr == null)
			{
				status = "no SpriteRenderer";
				return;
			}
		}
		Sprite sp = sr.sprite;
		if (!Active)
		{
			status = "switched off";
			Hide();
			return;
		}
		if (sp == null)
		{
			status = "no sprite";
			Hide();
			return;
		}
		if (m_BoneTransforms == null || m_BoneTransforms.Length == 0)
		{
			status = "no bones assigned";
			Hide();
			return;
		}
		if (sp != builtFor)
		{
			builtFor = sp;
			built = Build(sp);
			drawnOnce = false;
		}
		if (!built)
		{
			Hide();
			return;
		}

		// keep the copy in sync with the original SpriteRenderer
		sr.forceRenderingOff = true;
		mr.enabled = sr.enabled;
		mr.sortingLayerID = sr.sortingLayerID;
		mr.sortingOrder = sr.sortingOrder;
		if (mr.sharedMaterial != sr.sharedMaterial)
		{
			mr.sharedMaterial = sr.sharedMaterial;
			colorProp = (mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_RendererColor")) ? "_RendererColor" : "_Color";
			shownColor = new Color(-1f, -1f, -1f, -1f);
		}
		Color c = sr.color;
		if (c != shownColor)
		{
			shownColor = c;
			block.SetColor(colorProp, c);
			mr.SetPropertyBlock(block);
		}

		// off-screen characters only refresh now and then (saves CPU)
		frame++;
		if (drawnOnce && !mr.isVisible && (frame & 15) != 0)
		{
			return;
		}
		Deform();
		drawnOnce = true;
	}

	private bool Build(Sprite sp)
	{
		try
		{
			var bindPoses = sp.GetBindPoses();
			var boneWeights = sp.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);
			Vector2[] verts = sp.vertices;
			Vector2[] uvs = sp.uv;
			ushort[] tris = sp.triangles;

			if (bindPoses.Length == 0)
			{
				status = "sprite has no bones (drawn normally)";
				return false;
			}
			if (bindPoses.Length != m_BoneTransforms.Length)
			{
				status = "bone mismatch: sprite " + bindPoses.Length + " vs scene " + m_BoneTransforms.Length;
				return false;
			}
			if (boneWeights.Length != verts.Length)
			{
				status = "weights " + boneWeights.Length + " vs vertices " + verts.Length;
				return false;
			}

			int n = verts.Length;
			src = new Vector3[n];
			dst = new Vector3[n];
			weights = new BoneWeight[n];
			int boneCount = bindPoses.Length;
			for (int i = 0; i < n; i++)
			{
				src[i] = new Vector3(verts[i].x, verts[i].y, 0f);
				BoneWeight w = boneWeights[i];
				// guard against indices outside the bone list
				if (w.boneIndex0 < 0 || w.boneIndex0 >= boneCount) { w.weight0 = 0f; w.boneIndex0 = 0; }
				if (w.boneIndex1 < 0 || w.boneIndex1 >= boneCount) { w.weight1 = 0f; w.boneIndex1 = 0; }
				if (w.boneIndex2 < 0 || w.boneIndex2 >= boneCount) { w.weight2 = 0f; w.boneIndex2 = 0; }
				if (w.boneIndex3 < 0 || w.boneIndex3 >= boneCount) { w.weight3 = 0f; w.boneIndex3 = 0; }
				weights[i] = w;
			}
			bind = new Matrix4x4[boneCount];
			palette = new Matrix4x4[boneCount];
			for (int i = 0; i < boneCount; i++)
			{
				bind[i] = bindPoses[i];
			}
			int[] indices = new int[tris.Length];
			for (int i = 0; i < tris.Length; i++)
			{
				indices[i] = tris[i];
			}
			Color32[] white = new Color32[n];
			for (int i = 0; i < n; i++)
			{
				white[i] = new Color32(255, 255, 255, 255);
			}

			if (meshGo == null)
			{
				meshGo = new GameObject("SoftSkinMesh");
				meshGo.layer = gameObject.layer;
				meshGo.transform.SetParent(transform, false);
				mf = meshGo.AddComponent<MeshFilter>();
				mr = meshGo.AddComponent<MeshRenderer>();
				mr.shadowCastingMode = ShadowCastingMode.Off;
				mr.receiveShadows = false;
				mr.lightProbeUsage = LightProbeUsage.Off;
				mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
				block = new MaterialPropertyBlock();
				mesh = new Mesh();
				mesh.name = "SoftSkin " + name;
				mesh.MarkDynamic();
				mf.sharedMesh = mesh;
			}
			mesh.Clear();
			mesh.vertices = src;
			mesh.uv = uvs;
			mesh.colors32 = white;
			mesh.triangles = indices;
			mesh.RecalculateBounds();

			block.SetTexture("_MainTex", sp.texture);
			mr.SetPropertyBlock(block);
			shownColor = new Color(-1f, -1f, -1f, -1f);
			mr.sharedMaterial = null; // forces the material sync above to run
			status = "OK  " + boneCount + " bones, " + n + " vertices";
			return true;
		}
		catch (System.Exception e)
		{
			status = "ERROR " + e.GetType().Name + ": " + e.Message;
			return false;
		}
	}

	private void Deform()
	{
		Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
		for (int i = 0; i < palette.Length; i++)
		{
			Transform b = m_BoneTransforms[i];
			palette[i] = b != null ? worldToLocal * b.localToWorldMatrix * bind[i] : Matrix4x4.identity;
		}
		bool flipX = sr.flipX;
		bool flipY = sr.flipY;
		for (int i = 0; i < src.Length; i++)
		{
			BoneWeight w = weights[i];
			Vector3 p = src[i];
			float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
			Vector3 r;
			if (sum <= 0.0001f)
			{
				r = p;
			}
			else
			{
				r = palette[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0;
				if (w.weight1 > 0f) { r += palette[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1; }
				if (w.weight2 > 0f) { r += palette[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2; }
				if (w.weight3 > 0f) { r += palette[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3; }
				if (sum < 0.99f || sum > 1.01f) { r /= sum; }
			}
			r.z = 0f;
			if (flipX) { r.x = -r.x; }
			if (flipY) { r.y = -r.y; }
			dst[i] = r;
		}
		mesh.vertices = dst;
		mesh.RecalculateBounds();
	}
}
