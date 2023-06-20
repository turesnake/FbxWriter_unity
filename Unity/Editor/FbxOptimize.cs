using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
// using UnityEditorInternal;
using Fbx;

/// <summary>
/// 直接删除 .fbx 文件内的 color, uv2. uv3, uv4 数据段, 然后立刻 re-import 到 unity;
/// </summary>
public class FbxOptimize
{
    static bool isBuildBinary = true;
    static bool isBuildAscii = false;


    static int errorFileNum = 0;    // 有的 fbx 文件暂不支持,(库存在不足), 此时会放弃改写这个 fbx 文件, 并累加于此;
    static int successFileNum = 0;  // 成功改写的 fbx 文件个数
    static int noNeedChangeFileNum = 0; // 有些文件不需要修改 的文件个数


    // fbx 文件格式中: Objects -> Geometry -> Layer
    public class Layer
    {
        public FbxNode node;
        public int idx; 
        public List<LayerElement> layerElements = null;

        public Layer( FbxNode node_, int idx_ ) 
        {
            Debug.Assert( node_ != null );
            node = node_;
            idx = idx_;
            layerElements = LayerElement.FindLayerElementList( node.Nodes );
        }
        
        public int RemoveTgtLayerElement( string tgtLayerElementType_, int tgtLayerElementIndex_ ) 
        {
            int removeNum = 0;
            for( int i=layerElements.Count-1; i>=0; i-- ) 
            {
                if( layerElements[i].Type == tgtLayerElementType_ && layerElements[i].TypedIndex == tgtLayerElementIndex_ ) 
                {
                    node.Nodes.RemoveAt( layerElements[i].idx );
                    layerElements.RemoveAt( i );
                    removeNum++;
                }
            }
            return removeNum;
        }

        // nodes_: node_Geometry
        public static List<Layer> FindLayerList( List<FbxNode> nodes_ ) 
        {
            Debug.Assert( nodes_ != null );
            List<Layer> ret = new List<Layer>();

            for( int i=0; i<nodes_.Count; i++ )
            {
                FbxNode e = nodes_[i];
                if( e!=null && e.Name == "Layer" ) 
                {
                    ret.Add( new Layer(e,i) );
                }
            }
            return ret;
        }
    }



    // fbx 文件格式中: Objects -> Geometry -> Layer -> LayerElement
    public class LayerElement 
    {
        public FbxNode node;
        public int idx; // LayerElement 在 Layer 中的 idx; 为了方便删除
        public string Type;
        public int TypedIndex;
        public LayerElement( FbxNode node_, int idx_ ) 
        {
            Debug.Assert( node_ != null );
            node = node_;
            idx = idx_;
            bool isSet_Type = false; 
            bool isSet_TypedIndex = false;
            foreach( var e in node.Nodes ) 
            {
                if( e != null ) 
                {
                    switch( e.Name ) 
                    {
                        case "Type":       isSet_Type = true;        Type        = (string)e.Value; break;
                        case "TypedIndex": isSet_TypedIndex = true;  TypedIndex  = (int)e.Value; break;
                        default: break;
                    }
                }
            }
            Debug.Assert( isSet_Type == true && isSet_TypedIndex == true );
        }

        public static List<LayerElement> FindLayerElementList( List<FbxNode> nodes_ ) 
        {
            Debug.Assert( nodes_ != null );
            List<LayerElement> ret = new List<LayerElement>();

            for( int i=0; i<nodes_.Count; i++ ) 
            {
                FbxNode e = nodes_[i];
                if( e!=null && e.Name == "LayerElement" ) 
                {
                    ret.Add( new LayerElement(e, i) );
                }
            }
            return ret;
        }
    }



    // fbx 文件格式中: Objects -> Geometry -> 中的某些个元素, 比如 "LayerElementUV"
    public class OuterLayerElement 
    {
        public FbxNode node;
        public int idxInGeometry; // LayerElement 在 Geometry 中的 idx; 为了方便删除

        public int idxForDuplicate; // 有些元素, 如 "LayerElementUV", 可能有数个, 按照找到的次序, 依次提供 0,1,2... 的序号

        public OuterLayerElement( FbxNode node_, int idxInGeometry_, int idxForDuplicate_ ) 
        {
            Debug.Assert( node_ != null );
            node = node_;
            idxInGeometry = idxInGeometry_;
            idxForDuplicate = idxForDuplicate_;
        }

        // nodes_: Geometry.Nodes
        public static List<OuterLayerElement> FindOuterLayerElement( List<FbxNode> nodes_, string tgtName_ ) 
        {
            Debug.Assert( nodes_ != null );
            List<OuterLayerElement> ret = new List<OuterLayerElement>();
            int idxForDuplicate = 0;

            for( int i=0; i<nodes_.Count; i++ ) 
            {
                FbxNode e = nodes_[i];
                if( e!=null && e.Name == tgtName_ ) 
                {
                    ret.Add( new OuterLayerElement( e, i, idxForDuplicate ) );
                    idxForDuplicate++;
                }
            }
            return ret;
        }
    }


    [MenuItem("Tools/FBX 文件优化/生成 ascii 版 fbx 文件 (Debug用)")]
    public static void FbxOptimize_BuildAscii()
    {
        HashSet<string> retAssetPaths = HandleSelected();
        foreach( var path in retAssetPaths ) 
        {
            DoRemove( path, false, true, new List<string>(){} );
        }
    }

    [MenuItem("Tools/FBX 文件优化/删除 Color 数据")]
    public static void FbxOptimize_Remove_Color()
    {
        FbxBinaryReader.readDebugLog = "";
        
        HashSet<string> retAssetPaths = HandleSelected();
        foreach( var path in retAssetPaths ) 
        {
            //DoRemove( path, isBuildBinary, isBuildAscii, new List<string>(){ "Visibility" } );
            DoRemove( path, isBuildBinary, isBuildAscii, new List<string>(){ "Color"} );
        }
        HandleEnd(retAssetPaths);
        Debug.Log( "FbxWriter: readDebugLog = " + FbxBinaryReader.readDebugLog );
    }

    [MenuItem("Tools/FBX 文件优化/删除 UV1 数据; (此时会自动删除 UV2,UV3,UV4 数据)")]
    public static void FbxOptimize_Remove_UV1()
    {
        HashSet<string> retAssetPaths = HandleSelected();
        foreach( var path in retAssetPaths ) 
        {
            DoRemove( path, isBuildBinary, isBuildAscii, new List<string>(){"UV4","UV3","UV2","UV1"} ); // 必须倒着删
        }
        HandleEnd(retAssetPaths);
    }

    [MenuItem("Tools/FBX 文件优化/删除 UV2 数据; (此时会自动删除 UV3,UV4 数据)")]
    public static void FbxOptimize_Remove_UV2()
    {
        HashSet<string> retAssetPaths = HandleSelected();
        foreach( var path in retAssetPaths ) 
        {
            DoRemove( path, isBuildBinary, isBuildAscii, new List<string>(){"UV4","UV3","UV2"} ); // 必须倒着删
        }
        HandleEnd(retAssetPaths);
    }

    [MenuItem("Tools/FBX 文件优化/删除 UV3,UV4 数据")]
    public static void FbxOptimize_Remove_UV3()
    {
        HashSet<string> retAssetPaths = HandleSelected();
        foreach( var path in retAssetPaths ) 
        {
            DoRemove( path, isBuildBinary, isBuildAscii, new List<string>(){"UV4","UV3"} ); // 必须倒着删
        }
        HandleEnd(retAssetPaths);
    }



    static HashSet<string> HandleSelected() 
    {
        errorFileNum = 0;
        successFileNum = 0;
        noNeedChangeFileNum = 0;
        // -------
        HashSet<string> allAssetPaths = new HashSet<string>();

        if( Selection.objects.Length <= 0 ) 
        {
            UnityEditor.EditorUtility.DisplayDialog( "Error", "请先选择一个或多个 fbx文件 或 目录", "OK" );
            return allAssetPaths;
        }

        foreach( var obj in Selection.objects ) 
        {
            FindAllAssetPaths( obj, ref allAssetPaths );
        }

        // -----------------------
        if( allAssetPaths.Count <= 0 ) 
        {
            UnityEditor.EditorUtility.DisplayDialog( "Error", "您的选择内不包含任何 .fbx 文件", "Yes", "No" );
            return allAssetPaths;
        }
        // ---------- 将所有 fbx 文件信息 打印出来 ------------
        string log1 = "========================== FBX 文件优化 ===============================\n 即将检查如下 " + allAssetPaths.Count + "个 fbx 文件:";
        foreach( var path in allAssetPaths ) 
        {
            log1 += "\n   " + path;
        }
        Debug.Log( log1 );

        // ---------- 询问玩家是否修改这些 fbx 文件 -------------
        log1 = "您想改写: " + allAssetPaths.Count + " 个 fbx 文件:";
        int showNum = Math.Min( 5, allAssetPaths.Count );
        int i = 0;
        foreach( var p in allAssetPaths ) 
        {
            log1 += "\n  " + p;
            i++;
            if( i >= showNum )
            {
                break;
            }
        }
        if( allAssetPaths.Count > showNum ) 
        {
            log1 += "\n  ......";
        }
        bool ret = UnityEditor.EditorUtility.DisplayDialog( "提示", log1, "Yes", "No" );
        if( ret == false ) 
        {
            allAssetPaths.Clear();
        }

        return allAssetPaths;
    }




    static void FindAllAssetPaths( UnityEngine.Object obj, ref HashSet<string> assetPaths_ ) 
    {
        var selectedPath = AssetDatabase.GetAssetPath( obj.GetInstanceID() ); // 文件path 或 目录path, 以 "Assets/" 开头
        bool isDirectoryExists = System.IO.Directory.Exists( selectedPath );
        bool isFileExists = System.IO.File.Exists( selectedPath );
        
        if( !(isDirectoryExists || isFileExists) ) 
        {
            Debug.LogWarning( "某个选择的对象 既不是文件也不是目录: " + obj.name );
            return;
        }

        // ==================
        if( isFileExists ) 
        {
            // ==== 选中了单个文件 =====
            if( selectedPath.EndsWith(".fbx",true,null) ) 
            {
                selectedPath = selectedPath.Replace("/", "\\");
                if( assetPaths_.Contains( selectedPath ) == false ) 
                {
                    assetPaths_.Add( selectedPath );
                }
            }
        }
        else if( isDirectoryExists )
        {
            // ==== 选中了一个目录 =====
            GetRecursiveFiles(selectedPath, ref assetPaths_ );
        }
    }


    static void HandleEnd( HashSet<string> retAssetPaths_ )
    {
        if( retAssetPaths_.Count > 0 ) 
        {

            if( noNeedChangeFileNum>0 && successFileNum==0 && errorFileNum==0 ) 
            {
                UnityEditor.EditorUtility.DisplayDialog( "提示", "所有 fbx 文件 都符合要求, 无需改动", "Yes", "No" );
                return;
            }

            string log =      "成功改写: " + successFileNum + " 个 fbx 文件;" 
                        + "\n无需修改: " + noNeedChangeFileNum + " 个 fbx 文件;"
                        + "\n放弃改写: " + errorFileNum + " 个 fbx 文件;";
                    
            if( errorFileNum > 0 ) 
            {
                log += "\n\n 请到 console 中查看 放弃修改的文件信息";
            }
                            
            UnityEditor.EditorUtility.DisplayDialog( "提示", log, "Yes", "No" );
        }
    }



    static void GetRecursiveFiles( string folderFullPath_, ref HashSet<string> fileList )
	{
		try
		{
			IEnumerable<string> files = Directory.GetFiles( folderFullPath_, "*.*", SearchOption.AllDirectories).Where( s => s.EndsWith(".fbx",true,null) );
			foreach (string f in files)
			{
                string f2 = f.Replace("/", "\\");
                if( fileList.Contains(f2) == false ) 
                {
                    fileList.Add(f2);
                }
			}
			return;
		}
		catch (UnauthorizedAccessException UAEx)
		{
			Console.WriteLine(UAEx.Message);
		}
		catch (PathTooLongException PathEx)
		{
			Console.WriteLine(PathEx.Message);
		}
		return;
	}




    // assetPath_: 从 "Assets/" 开始的 文件的相对path 
    static void DoRemove( string assetPath_, bool isBuildBinary_, bool isBuildAscii_, List<string> doWhats_ )
	{
        string asciiPath = assetPath_.Substring(0,assetPath_.Length-4) + "_str.fbx"; // debug 用的
        string testPath = assetPath_.Substring(0,assetPath_.Length-4) + "_test.fbx"; // debug 用的
        bool isError = false;

        Debug.Log( "=== 开始处理文件: " + assetPath_ + " ==========" );

        Fbx.FbxDocument documentNode = null;
		// Read a file
        try
        {
            documentNode = FbxIO.ReadBinary( assetPath_ );
        }
        catch( Exception ex )
        {
            isError = true;
            errorFileNum++;
            Debug.LogWarning( "!!!!! 读取文件时异常, 放弃优化: " + assetPath_ 
                                + "\n Message: " + ex.Message 
                                + "\n StackTrace: " + ex.StackTrace 
                                + "\n Source: " + ex.Source 
            );
            return;
        }
        Debug.Assert( documentNode != null );

		// // Update a property
		// documentNode["Creator"].Value = "My Application";

        // ================================================================
        FbxNode node_Objects = FindUniqueFbxNode( documentNode.Nodes, "Objects" );
        
        List<FbxNode> nodes_Geometry = FindFbxNodeList( node_Objects.Nodes, "Geometry" );
        if( nodes_Geometry.Count <= 0 ) 
        {
            noNeedChangeFileNum++;
            Debug.LogWarning( "~~~ 没找到 Geometry 段, 放弃修改: " + assetPath_ );
        }
        else 
        {
            // ========================================:
            bool isChange = false;
            foreach( var doWhat in doWhats_ ) 
            {
                switch( doWhat.ToLower() ) 
                {
                    case "visibility":   isChange |= Delete_LayerElementVisibility( nodes_Geometry, assetPath_ );     break;
                    case "color":       isChange |= Delete_Color( nodes_Geometry, assetPath_ );     break;
                    case "uv1":         isChange |= Delete_UV(    nodes_Geometry, 1, assetPath_ );  break;
                    case "uv2":         isChange |= Delete_UV(    nodes_Geometry, 2, assetPath_ );  break;
                    case "uv3":         isChange |= Delete_UV(    nodes_Geometry, 3, assetPath_ );  break;
                    case "uv4":         isChange |= Delete_UV(    nodes_Geometry, 4, assetPath_ );  break;
                    default: 
                        Debug.Assert( false, "错误参数: " + doWhat );
                        break;
                }
            }

            // ========================================
            // Preview the file in the console
            // var writer = new FbxAsciiWriter(Console.OpenStandardOutput());
            // writer.Write(documentNode);

            bool isNeedRefresh = false;

            // debug 用:
            if(isBuildAscii_ == true) 
            {
                FbxIO.WriteAscii(documentNode, asciiPath );
                isNeedRefresh = true;
            }


            if( isChange ) 
            {
                // Write the updated binary
                if( isBuildBinary_ == true )
                {
                    
                    try
                    {
                        FbxIO.WriteBinary(documentNode, testPath );
                    }
                    catch( Exception ex )
                    {
                        isError = true;
                        errorFileNum++;
                        Debug.LogWarning( "!!!!! 文件异常, 放弃优化: " + assetPath_ 
                                            + "\n Message: " + ex.Message 
                                            + "\n StackTrace: " + ex.StackTrace 
                                            + "\n Source: " + ex.Source 
                        );
                    }

                    File.Delete(testPath);

                    if( isError == false ) 
                    {
                        successFileNum++;
                        FbxIO.WriteBinary(documentNode, assetPath_ );
                        Debug.Log( "覆写原 二进制文件 成功" );
                        isNeedRefresh = true;
                    }
                }
            }
            else 
            {
                noNeedChangeFileNum++;
            }

            // ====== 重新加载 fbx 文件 =======
            if( isNeedRefresh == true ) 
            {
                AssetDatabase.Refresh(); // 能让这个 fbx 文件被 unity 重新 import, 且保留上次的 import settings;
            }
        }

        Debug.Log( "--- 处理完毕: " + assetPath_     + " ----------" );
	}


    // LayerElementVisibility
    static bool Delete_LayerElementVisibility( List<FbxNode> nodes_Geometry_, string fbxName_ ) 
    {
        int totalRemoveNum = 0;
        foreach( var node_Geometry in nodes_Geometry_ ) 
        {
            if( node_Geometry != null ) 
            {
                // ---  删除 Geometry - LayerElementVisibility
                int removeNum1 = DeleteFbxNode( node_Geometry.Nodes, "LayerElementVisibility" );

                // --- 删除 Geometry - Layer 0 中 LayerElement == "LayerElementVisibility"
                List<Layer> layers = Layer.FindLayerList( node_Geometry.Nodes );
                int removeNum2 = 0;
                for( int i=layers.Count-1; i>=0; i-- ) 
                {
                    removeNum2 += layers[i].RemoveTgtLayerElement( "LayerElementVisibility", 0 );
                }

                if( removeNum1 + removeNum2 > 0 )
                {
                    Debug.Log( "删除 LayerElementVisibility: outerNum: " + removeNum1 + "; innNum: " + removeNum2 + "; \n" + fbxName_ );
                    totalRemoveNum += removeNum1 + removeNum2;
                }
            }
        }
        return totalRemoveNum > 0;
    }





    static bool Delete_Color( List<FbxNode> nodes_Geometry_, string fbxName_ ) 
    {
        int totalRemoveNum = 0;
        foreach( var node_Geometry in nodes_Geometry_ ) 
        {
            if( node_Geometry != null ) 
            {
                // ---  删除 Geometry - LayerElementColor
                int removeNum1 = DeleteFbxNode( node_Geometry.Nodes, "LayerElementColor" );

                // --- 删除 Geometry - Layer 0 中 LayerElement == "LayerElementColor"
                List<Layer> layers = Layer.FindLayerList( node_Geometry.Nodes );
                int removeNum2 = 0;
                for( int i=layers.Count-1; i>=0; i-- ) 
                {
                    removeNum2 += layers[i].RemoveTgtLayerElement( "LayerElementColor", 0 );
                }

                if( removeNum1 + removeNum2 > 0 )
                {
                    Debug.Log( "删除 Color: outerNum: " + removeNum1 + "; innNum: " + removeNum2 + "; \n" + fbxName_ );
                    totalRemoveNum += removeNum1 + removeNum2;
                }
            }
        }
        return totalRemoveNum > 0;
    }


    static bool Delete_UV( List<FbxNode> nodes_Geometry_, int uvIndex_, string fbxName_ ) 
    {
        int totalRemoveNum = 0;
        foreach( var node_Geometry in nodes_Geometry_ ) 
        {
            if( node_Geometry != null ) 
            {
                // ---  删除 Geometry - LayerElementUV
                List<OuterLayerElement> outerLayerElements = OuterLayerElement.FindOuterLayerElement( node_Geometry.Nodes, "LayerElementUV" );
                int removeNum1 = 0;

                for( int i=outerLayerElements.Count-1; i>=0; i-- ) 
                {
                    if( outerLayerElements[i].idxForDuplicate == uvIndex_ ) 
                    {
                        node_Geometry.Nodes.RemoveAt( outerLayerElements[i].idxInGeometry );
                        outerLayerElements.RemoveAt( i );
                        removeNum1++;
                    }
                }

                // --- 删除 Geometry - Layer uvIndex_ 中 LayerElement == "LayerElementUV"
                List<Layer> layers = Layer.FindLayerList( node_Geometry.Nodes );
                int removeNum2 = 0;
                for( int i=layers.Count-1; i>=0; i-- ) 
                {
                    int ret = layers[i].RemoveTgtLayerElement( "LayerElementUV", uvIndex_ );

                    // 若这个 layer 空了, 就连这个 layer 一并删除:
                    if( ret > 0 && layers[i].layerElements.Count == 0 ) 
                    {
                        node_Geometry.Nodes.RemoveAt( layers[i].idx );
                        layers.RemoveAt( i );
                        removeNum2++;
                    }
                }

                if( removeNum1 + removeNum2 > 0 )
                {
                    Debug.Log( "删除 UV" + uvIndex_ + ": outerNum: " + removeNum1 + "; innNum: " + removeNum2 + "; \n" + fbxName_ );
                    totalRemoveNum += removeNum1 + removeNum2;
                }
            }
        }
        return totalRemoveNum > 0;
    }





    static FbxNode FindUniqueFbxNode( List<FbxNode> nodes_, string tgtName_ ) 
    {
        Debug.Assert( nodes_ != null );
        FbxNode ret = null;
        foreach( var e in nodes_ ) 
        {
            if( e!=null && e.Name == tgtName_ ) 
            {
                if( ret != null ) 
                {
                    Debug.LogError( "发现数个节点: " + tgtName_ );
                }
                ret = e;
            }   
        }
        return ret;
    }


    static int DeleteFbxNode( List<FbxNode> nodes_, string tgtName_ ) 
    {
        int deleteNum = 0;
        Debug.Assert( nodes_ != null );
        for( int i=nodes_.Count-1; i>=0; i-- ) 
        {
            if( nodes_[i]!=null && nodes_[i].Name == tgtName_ ) 
            {
                nodes_.RemoveAt( i );
                deleteNum++;
            }
        }
        return deleteNum;
    }

    // nodes_: node_Layer.Nodes
    // static int DeleteLayerElement( List<FbxNode> nodes_, string tgtLayerElementName_ ) 
    // {
    //     int deleteNum = 0;
    //     Debug.Assert( nodes_ != null );
    //     for( int i=nodes_.Count-1; i>=0; i-- ) 
    //     {
    //         if( nodes_[i] != null && nodes_[i].Name == "LayerElement" ) 
    //         {
    //             bool isTgt = false;
    //             foreach( var e in nodes_[i].Nodes ) 
    //             {
    //                 if( e!=null && e.Name == "Type" && (string)e.Value == tgtLayerElementName_ ) 
    //                 {
    //                     isTgt = true;
    //                 }
    //             }

    //             if( isTgt == true ) 
    //             {
    //                 nodes_.RemoveAt( i );
    //                 deleteNum++;
    //             }
    //         }
    //     }
    //     return deleteNum;
    // }




    static List<FbxNode> FindFbxNodeList( List<FbxNode> nodes_, string tgtEntName_ ) 
    {
        Debug.Assert( nodes_ != null );
        List<FbxNode> ret = new List<FbxNode>();
        foreach( var e in nodes_ ) 
        {
            if( e!=null && e.Name == tgtEntName_ ) 
            {
                ret.Add( e );
            }
        }
        return ret;
    }


   
}
