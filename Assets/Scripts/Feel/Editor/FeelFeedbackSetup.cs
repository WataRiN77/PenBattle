#if UNITY_EDITOR
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 一键配置 Feel 反馈。
///
/// 菜单：Tools → PenBattle → 配置 Feel 反馈（一键）
///
/// 做三件事：
///   1. 在场景里建 5 个 MMF_Player 物体（已存在同名物体则复用，不会重复建）
///   2. 往每个里面放好对应的 Feedback，并把能确定字段名的那几个数值填上
///   3. 自动把 5 个引用填进场景里的 FeelFeedbackBridge
///
/// 剩下需要手动做的（脚本做不了，因为要选你自己的资源）：
///   · MMF_AudioSource 指定音效、MMF_ParticlesInstantiation 指定粒子预制体
///   · 每个 MMF_Player 上关掉 AutoPlay（改用 OnDemand）
public static class FeelFeedbackSetup
{
	[MenuItem("Tools/PenBattle/配置 Feel 反馈（一键）")]
	public static void Setup()
	{
		// ---------- 1. 5 个 MMF_Player ----------
		MMF_Player hitWorld = GetOrCreate("MMF_HitWorld");
		MMF_Player hitScreen = GetOrCreate("MMF_HitScreen");
		MMF_Player obstacle = GetOrCreate("MMF_Obstacle");
		MMF_Player eliminate = GetOrCreate("MMF_Eliminate");
		MMF_Player launch = GetOrCreate("MMF_Launch");

		// ---------- 2. 往里面放 Feedback ----------
		// 说明：项目目前**还没有音频/粒子资源**，所以这里只放"不需要任何资源"的反馈。
		//       MMF_AudioSource / MMF_ParticlesInstantiation / MMF_AudioFilterLowPass
		//       会在下一步被主动清理掉 —— 等你有资源了自己加回来即可。

		// 命中·屏幕层：震动 + 顿帧
		// ⚠ 用 MMF_PositionShake 而不是 MMF_CameraShake —— 相机是 Cinemachine 驱动的，
		//   直接改 Main Camera 的 transform 会被它每帧 LateUpdate 覆盖（震动看不见）。
		//   下面 2.6 步还会把这个物体挂到虚拟相机下，震动才真正传得出去。
		RemoveFeedbackOfType(hitScreen, typeof(MMF_CameraShake));
		AddIfEmpty(hitScreen, typeof(MMF_PositionShake));
		if (AddIfEmpty(hitScreen, typeof(MMF_FreezeFrame)) is MMF_FreezeFrame hitFreeze)
		{
			hitFreeze.FreezeFrameDuration = 0.06f;   // 60ms ≈ 4 帧，主观上才看得见（30ms 太短）
		}

		// 淘汰：慢动作(0.3× / 0.8s) + 顿帧(80ms) + 强震
		if (AddIfEmpty(eliminate, typeof(MMF_TimescaleModifier)) is MMF_TimescaleModifier slow)
		{
			slow.TimeScale = 0.3f;
			slow.TimeScaleDuration = 0.8f;
			slow.TimeScaleLerp = true;
			slow.TimescaleLerpDuration = 0.1f;
		}

		if (AddIfEmpty(eliminate, typeof(MMF_FreezeFrame)) is MMF_FreezeFrame elimFreeze)
		{
			elimFreeze.FreezeFrameDuration = 0.08f;
		}

		RemoveFeedbackOfType(eliminate, typeof(MMF_CameraShake));
		AddIfEmpty(eliminate, typeof(MMF_PositionShake));

		// 命中·世界层 / 撞障碍 / 发射：暂时留空（等有音频、粒子资源再填）
		// 想给发射加"笔身拉伸"，把 MMF_Scale 挂到笔预制体上比挂这里合适

		// ---------- 2.5 清理掉"需要资源但目前用不上"的 Feedback ----------
		StripAssetDependentFeedbacks(hitWorld);
		StripAssetDependentFeedbacks(hitScreen);
		StripAssetDependentFeedbacks(obstacle);
		StripAssetDependentFeedbacks(eliminate);
		StripAssetDependentFeedbacks(launch);

		// ---------- 2.6 屏幕震动：给虚拟相机加"震动接收器" ----------
		// 关键认知：MMF_PositionShake **自己不抖任何东西**！
		//   源码注释原话："This feedback lets you emit a PositionShake event.
		//                  This will be caught by MMPositionShakers (on the specified channel)."
		// 也就是说它只是**发一个震动事件**，必须有 MMPositionShaker 组件接住才会真的抖。
		// 少了接收器 → 事件发出去没人接 → 肉眼完全看不出（本次就是这个问题）。
		//
		// 为什么放在虚拟相机上：相机由 Cinemachine 驱动，直接抖 Main Camera 会被它每帧覆盖；
		// 抖虚拟相机，Cinemachine 才会把它当作真实相机位置读走。
		var vcam = Object.FindObjectOfType<Cinemachine.CinemachineVirtualCamera>();
		if (vcam != null)
		{
			hitScreen.transform.SetParent(vcam.transform, false);
			eliminate.transform.SetParent(vcam.transform, false);

			// 注意：MMPositionShaker 的命名空间是 MoreMountains.Feedbacks（不是 MoreMountains.Tools）
			var shaker = vcam.GetComponent<MMPositionShaker>();
			if (shaker == null)
			{
				shaker = Undo.AddComponent<MMPositionShaker>(vcam.gameObject);
				Debug.Log($"[Feel 配置] 已给虚拟相机「{vcam.name}」加上 MMPositionShaker（震动接收器）。");
			}

			// 振动参数 + 直接指定接收器（不走 channel 广播，少一层出错可能）。
			//
			// 数值原则：
			//   · **命中**：发生频繁 → 要"短促、幅度小"，干脆利落；调大会很快让人晕。
			//   · **淘汰**：一局才几次 → 可以"长、幅度大"，做出仪式感。
			// （之前那组 0.25/0.8、0.55/1.5 是为了确认通路故意调大的。）
			ConfigureShake(hitScreen, shaker, duration: 0.18f, speed: 28f, range: 0.30f);
			ConfigureShake(eliminate, shaker, duration: 0.50f, speed: 35f, range: 1.00f);
		}
		else
		{
			Debug.LogWarning("[Feel 配置] 场景里没找到 CinemachineVirtualCamera —— " +
							 "请手动把 MMF_HitScreen / MMF_Eliminate 拖到虚拟相机下，" +
							 "并在虚拟相机上 Add Component → MMPositionShaker，否则震动看不见。");
		}

		// ---------- 3. 自动填进 FeelFeedbackBridge ----------
		var bridge = Object.FindObjectOfType<FeelFeedbackBridge>();
		if (bridge == null)
		{
			Debug.LogWarning("[Feel 配置] 场景里找不到 FeelFeedbackBridge —— 5 个 MMF_Player 已建好，" +
							 "请先挂上脚本、再跑一次本菜单即可自动填引用。");
		}
		else
		{
			bridge.hitWorldFeedback = hitWorld;
			bridge.hitScreenFeedback = hitScreen;
			bridge.obstacleHitFeedback = obstacle;
			bridge.eliminateFeedback = eliminate;
			bridge.launchFeedback = launch;

			EditorUtility.SetDirty(bridge);
			Debug.Log("[Feel 配置] 5 个 MMF_Player 已自动填进 FeelFeedbackBridge。");
		}

		EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
		Selection.activeObject = eliminate;

		Debug.Log("[Feel 配置] 完成！当前只配了**不需要任何资源**的反馈：\n" +
				  "  · MMF_HitScreen ：震屏 + 顿帧(30ms)\n" +
				  "  · MMF_Eliminate ：慢动作(0.3× / 0.8s) + 顿帧(80ms) + 强震\n" +
				  "  · MMF_HitWorld / MMF_Obstacle / MMF_Launch：暂时为空（等有音频、粒子资源再加）\n" +
				  "还需你手动做一件事：每个 MMF_Player 上关掉 AutoPlay（改用 OnDemand）。\n" +
				  "最后 Ctrl+S 保存场景。");
	}

	// ================= 工具 =================

	/// 场景里已有同名物体就复用，否则新建
	private static MMF_Player GetOrCreate(string name)
	{
		var existing = GameObject.Find(name);
		if (existing != null)
		{
			var p = existing.GetComponent<MMF_Player>();
			if (p != null) return p;

			return existing.AddComponent<MMF_Player>();
		}

		var go = new GameObject(name);
		Undo.RegisterCreatedObjectUndo(go, "Create " + name);
		return go.AddComponent<MMF_Player>();
	}

	/// 该类型还没有就添加（避免重复跑菜单时重复添加）
	private static MMF_Feedback AddIfEmpty(MMF_Player player, System.Type feedbackType)
	{
		var existing = FindFeedback(player, feedbackType);
		if (existing != null) return existing;

		var added = player.AddFeedback(feedbackType);
		EditorUtility.SetDirty(player);
		return added;
	}

	/// 注意：MMF_Player 只提供了泛型版的 GetFeedbackOfType<T>()，
	/// 没有接受 System.Type 的重载，所以这里直接遍历公开的 FeedbacksList。
	private static MMF_Feedback FindFeedback(MMF_Player player, System.Type type)
	{
		if (player.FeedbacksList == null) return null;

		foreach (var fb in player.FeedbacksList)
		{
			if (fb != null && fb.GetType() == type) return fb;
		}

		return null;
	}

	/// 这些 Feedback 需要音频 / 粒子等资源，当前项目还没有 → 主动从列表里摘掉
	private static readonly System.Type[] AssetDependentFeedbacks =
	{
		typeof(MMF_AudioSource),
		typeof(MMF_ParticlesInstantiation),
		typeof(MMF_AudioFilterLowPass),
	};

	private static void StripAssetDependentFeedbacks(MMF_Player player)
	{
		if (player.FeedbacksList == null) return;

		// 从后往前删，避免删完一个之后索引错位
		for (int i = player.FeedbacksList.Count - 1; i >= 0; i--)
		{
			var fb = player.FeedbacksList[i];
			if (fb == null) continue;
			if (System.Array.IndexOf(AssetDependentFeedbacks, fb.GetType()) < 0) continue;

			string removedName = fb.GetType().Name;
			player.RemoveFeedback(i);      // 按索引删（MMF_Player 只提供这个签名）
			Debug.Log($"[Feel 配置] 从 {player.name} 移除 {removedName}（缺对应资源，等有了再加）");
		}

		EditorUtility.SetDirty(player);
	}

	/// 移除指定类型的所有 Feedback（用于把旧配置换成新配置，例如 CameraShake → PositionShake）
	private static void RemoveFeedbackOfType(MMF_Player player, System.Type type)
	{
		if (player.FeedbacksList == null) return;

		for (int i = player.FeedbacksList.Count - 1; i >= 0; i--)
		{
			var fb = player.FeedbacksList[i];
			if (fb == null) continue;
			if (fb.GetType() != type) continue;

			string removedName = fb.GetType().Name;
			player.RemoveFeedback(i);
			Debug.Log($"[Feel 配置] 从 {player.name} 移除旧的 {removedName}（已换成新配置）");
		}

		EditorUtility.SetDirty(player);
	}

	/// 配置 MMF_PositionShake 的参数。
	/// 两个要点：
	///   ① **关掉 UseAttenuation** —— 否则"撞击点离相机远"会把强度衰减到几乎为 0（默认是开的！）
	///   ② **直接指定 TargetShaker** —— 不走 channel 广播，避免接收器频道不匹配导致没反应
	private static void ConfigureShake(MMF_Player player, MMPositionShaker shaker,
									   float duration, float speed, float range)
	{
		var shake = FindFeedback(player, typeof(MMF_PositionShake)) as MMF_PositionShake;
		if (shake == null) return;

		shake.Duration = duration;
		shake.ShakeSpeed = speed;
		shake.ShakeRange = range;                 // ★幅度：越大越明显
		shake.ShakeMainDirection = Vector3.up;
		shake.ShakeAltDirection = Vector3.right;
		shake.RandomizeDirection = true;
		shake.RandomizeDirectionOnPlay = true;
		shake.AddDirectionalNoise = true;
		shake.UseAttenuation = false;             // ★关键：关掉距离衰减
		shake.TargetShaker = shaker;              // ★关键：直连接收器

		EditorUtility.SetDirty(player);
		Debug.Log($"[Feel 配置] {player.name} 的 PositionShake：时长 {duration}s / 速度 {speed} / 幅度 {range}（已关衰减、已绑定接收器）");
	}
}
#endif
