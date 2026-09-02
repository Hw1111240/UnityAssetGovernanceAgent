using UnityEngine;
using System;
using AssetGovernanceAgent.Editor.Models;
using AssetGovernanceAgent.Editor.Rules;

namespace AssetGovernanceAgent.Editor.Services
{
    
    /// <summary>
    /// Texture Max Size安全修复服务。
    ///
    /// 当前阶段只实现DryRun：
    /// 读取并校验修复请求，但不修改TextureImporter。
    /// </summary>
    public class TextureMaxSizeFixService : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
        }
    }
}