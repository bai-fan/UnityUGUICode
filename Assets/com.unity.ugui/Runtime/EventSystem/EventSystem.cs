using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityEngine.EventSystems
{
    [AddComponentMenu("Event/Event System")]
    /// <summary>
    /// Handles input, raycasting, and sending events.
    /// 处理输入、射线检测和发送事件
    ///  注：EventSystem负责管理调度事件，控制各输入模块、射线投射以及事件动作的执行。UI的事件系统处理用户的交互动作，
    ///     通过BaseInput来获取用户输入的信息和状态，通过InputModule处理输入，产生和发送事件，通过RayCaster判断和
    ///     选择需要响应交互事件的对象，最终由ExecuteEvents执行相应的回调，调用EventSystemHandler，完成交互
    /// </summary>
    /// <remarks>
    /// The EventSystem is responsible for processing and handling events in a Unity scene. A scene should only contain one EventSystem. The EventSystem works in conjunction with a number of modules and mostly just holds state and delegates functionality to specific, overrideable components.
    /// When the EventSystem is started it searches for any BaseInputModules attached to the same GameObject and adds them to an internal list. On update each attached module receives an UpdateModules call, where the module can modify internal state. After each module has been Updated the active module has the Process call executed.This is where custom module processing can take place.
    /// </remarks>
    public class EventSystem : UIBehaviour
    {
        //系统输入模块
        private List<BaseInputModule> m_SystemInputModules = new List<BaseInputModule>();

        //当前输入模块
        private BaseInputModule m_CurrentInputModule;

        //处理所有输入的EventSystem
        private static List<EventSystem> m_EventSystems = new List<EventSystem>();

        /// <summary>
        /// Return the current EventSystem.
        /// </summary>
        public static EventSystem current
        {
            get { return m_EventSystems.Count > 0 ? m_EventSystems[0] : null; }
            set
            {
                int index = m_EventSystems.IndexOf(value);

                if (index >= 0)
                {
                    m_EventSystems.RemoveAt(index);
                    m_EventSystems.Insert(0, value);
                }
            }
        }

        [SerializeField]
        [FormerlySerializedAs("m_Selected")]
        private GameObject m_FirstSelected;

        [SerializeField]
        private bool m_sendNavigationEvents = true;

        /// <summary>
        /// Should the EventSystem allow navigation events (move / submit / cancel).
        /// </summary>
        public bool sendNavigationEvents
        {
            get { return m_sendNavigationEvents; }
            set { m_sendNavigationEvents = value; }
        }

        [SerializeField]
        private int m_DragThreshold = 10;

        /// <summary>
        /// The soft area for dragging in pixels.
        /// </summary>
        public int pixelDragThreshold
        {
            get { return m_DragThreshold; }
            set { m_DragThreshold = value; }
        }

        //当前选择GameObject
        private GameObject m_CurrentSelected;

        /// <summary>
        /// The currently active EventSystems.BaseInputModule.
        /// </summary>
        public BaseInputModule currentInputModule
        {
            get { return m_CurrentInputModule; }
        }

        /// <summary>
        /// Only one object can be selected at a time. Think: controller-selected button.
        /// </summary>
        public GameObject firstSelectedGameObject
        {
            get { return m_FirstSelected; }
            set { m_FirstSelected = value; }
        }

        /// <summary>
        /// The GameObject currently considered active by the EventSystem.
        /// </summary>
        public GameObject currentSelectedGameObject
        {
            get { return m_CurrentSelected; }
        }

        [Obsolete("lastSelectedGameObject is no longer supported")]
        public GameObject lastSelectedGameObject
        {
            get { return null; }
        }

        private bool m_HasFocus = true;

        /// <summary>
        /// Flag to say whether the EventSystem thinks it should be paused or not based upon focused state.
        /// </summary>
        /// <remarks>
        /// Used to determine inside the individual InputModules if the module should be ticked while the application doesnt have focus.
        /// </remarks>
        public bool isFocused
        {
            get { return m_HasFocus; }
        }

        protected EventSystem()
        { }

        /// <summary>
        /// Recalculate the internal list of BaseInputModules.
        /// 得到所有的BaseInputModule组件,遍历移除隐藏的或无效的组件
        /// </summary>
        public void UpdateModules()
        {
            GetComponents(m_SystemInputModules);
            for (int i = m_SystemInputModules.Count - 1; i >= 0; i--)
            {
                if (m_SystemInputModules[i] && m_SystemInputModules[i].IsActive())
                    continue;

                m_SystemInputModules.RemoveAt(i);
            }
        }

        private bool m_SelectionGuard;

        /// <summary>
        /// Returns true if the EventSystem is already in a SetSelectedGameObject.
        /// </summary>
        public bool alreadySelecting
        {
            get { return m_SelectionGuard; }
        }

        /// <summary>
        /// Set the object as selected. Will send an OnDeselect the the old selected object and OnSelect to the new selected object.
        /// 更新当前选择的对象属性
        /// </summary>
        /// <param name="selected">GameObject to select.</param>
        /// <param name="pointer">Associated EventData.</param>
        public void SetSelectedGameObject(GameObject selected, BaseEventData pointer)
        {
            if (m_SelectionGuard)
            {
                Debug.LogError("Attempting to select " + selected + "while already selecting an object.");
                return;
            }

            m_SelectionGuard = true;
            if (selected == m_CurrentSelected)
            {
                //如果当前选择对象和原始选择对象一样
                m_SelectionGuard = false;
                return;
            }

            //如果不一样，发送事件：取消选择原始对象，重新选择当前对象
            // Debug.Log("Selection: new (" + selected + ") old (" + m_CurrentSelected + ")");
            ExecuteEvents.Execute(m_CurrentSelected, pointer, ExecuteEvents.deselectHandler);
            m_CurrentSelected = selected;
            ExecuteEvents.Execute(m_CurrentSelected, pointer, ExecuteEvents.selectHandler);
            m_SelectionGuard = false;
        }

        private BaseEventData m_DummyData;
        private BaseEventData baseEventDataCache
        {
            get
            {
                if (m_DummyData == null)
                    m_DummyData = new BaseEventData(this);

                return m_DummyData;
            }
        }

        /// <summary>
        /// Set the object as selected. Will send an OnDeselect the the old selected object and OnSelect to the new selected object.
        /// </summary>
        /// <param name="selected">GameObject to select.</param>
        public void SetSelectedGameObject(GameObject selected)
        {
            SetSelectedGameObject(selected, baseEventDataCache);
        }

        /// <summary>
        /// 射线结果对象
        /// 1、根据摄像机深度比对
        /// 2、根据sortOrderPriority
        /// 3、根据renderOrderPriority
        /// 4、根据sortingLayer
        /// 5、根据sortingOrder
        /// 6、根据深度比对
        /// 7、根据distance距离比对
        /// 8、根据index
        /// </summary>
        private static int RaycastComparer(RaycastResult lhs, RaycastResult rhs)
        {
            if (lhs.module != rhs.module)
            {
                var lhsEventCamera = lhs.module.eventCamera;
                var rhsEventCamera = rhs.module.eventCamera;
                if (lhsEventCamera != null && rhsEventCamera != null && lhsEventCamera.depth != rhsEventCamera.depth)
                {
                    // need to reverse the standard compareTo
                    if (lhsEventCamera.depth < rhsEventCamera.depth)
                        return 1;
                    if (lhsEventCamera.depth == rhsEventCamera.depth)
                        return 0;

                    return -1;
                }

                if (lhs.module.sortOrderPriority != rhs.module.sortOrderPriority)
                    return rhs.module.sortOrderPriority.CompareTo(lhs.module.sortOrderPriority);

                if (lhs.module.renderOrderPriority != rhs.module.renderOrderPriority)
                    return rhs.module.renderOrderPriority.CompareTo(lhs.module.renderOrderPriority);
            }

            if (lhs.sortingLayer != rhs.sortingLayer)
            {
                // Uses the layer value to properly compare the relative order of the layers.
                var rid = SortingLayer.GetLayerValueFromID(rhs.sortingLayer);
                var lid = SortingLayer.GetLayerValueFromID(lhs.sortingLayer);
                return rid.CompareTo(lid);
            }

            if (lhs.sortingOrder != rhs.sortingOrder)
                return rhs.sortingOrder.CompareTo(lhs.sortingOrder);

            // comparing depth only makes sense if the two raycast results have the same root canvas (case 912396)
            if (lhs.depth != rhs.depth && lhs.module.rootRaycaster == rhs.module.rootRaycaster)
                return rhs.depth.CompareTo(lhs.depth);

            if (lhs.distance != rhs.distance)
                return lhs.distance.CompareTo(rhs.distance);

            return lhs.index.CompareTo(rhs.index);
        }

        private static readonly Comparison<RaycastResult> s_RaycastComparer = RaycastComparer;

        /// <summary>
        /// Raycast into the scene using all configured BaseRaycasters.
        /// RaycastAll在PointerInputModule的GetTouchPointerEventData和GetMousePointerEventData中调用
        /// 调用RaycasterManager.GetRaycasters()获取Canvas下所有对象进行射线检测
        /// 对所有投射对象进行排序
        /// </summary>
        /// <param name="eventData">Current pointer data.当前点击的点</param>
        /// <param name="raycastResults">List of 'hits' to populate.</param>
        public void RaycastAll(PointerEventData eventData, List<RaycastResult> raycastResults)
        {
            raycastResults.Clear();
            var modules = RaycasterManager.GetRaycasters();
            for (int i = 0; i < modules.Count; ++i)
            {
                var module = modules[i];
                if (module == null || !module.IsActive())
                    continue;

                module.Raycast(eventData, raycastResults);
            }

            raycastResults.Sort(s_RaycastComparer);
        }

        /// <summary>
        /// Is the pointer with the given ID over an EventSystem object?
        /// 判断是否点击在UI上，判断最后一次点击的EventData数据是否为空
        /// </summary>
        public bool IsPointerOverGameObject()
        {
            return IsPointerOverGameObject(PointerInputModule.kMouseLeftId);
        }

        /// <summary>
        /// Is the pointer with the given ID over an EventSystem object?
        /// </summary>
        /// <remarks>
        /// If you use IsPointerOverGameObject() without a parameter, it points to the "left mouse button" (pointerId = -1); therefore when you use IsPointerOverGameObject for touch, you should consider passing a pointerId to it
        /// Note that for touch, IsPointerOverGameObject should be used with ''OnMouseDown()'' or ''Input.GetMouseButtonDown(0)'' or ''Input.GetTouch(0).phase == TouchPhase.Began''.
        /// </remarks>
        /// <example>
        /// <code>
        /// using UnityEngine;
        /// using System.Collections;
        /// using UnityEngine.EventSystems;
        ///
        /// public class MouseExample : MonoBehaviour
        /// {
        ///     void Update()
        ///     {
        ///         // Check if the left mouse button was clicked
        ///         if (Input.GetMouseButtonDown(0))
        ///         {
        ///             // Check if the mouse was clicked over a UI element
        ///             if (EventSystem.current.IsPointerOverGameObject())
        ///             {
        ///                 Debug.Log("Clicked on the UI");
        ///             }
        ///         }
        ///     }
        /// }
        /// </code>
        /// </example>
        public bool IsPointerOverGameObject(int pointerId)
        {
            if (m_CurrentInputModule == null)
                return false;

            return m_CurrentInputModule.IsPointerOverGameObject(pointerId);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            //加入到m_EventSystems
            m_EventSystems.Add(this);
        }

        protected override void OnDisable()
        {
            if (m_CurrentInputModule != null)
            {
                //使当前BaseInputModule失效，清除已选择对象相关标记
                m_CurrentInputModule.DeactivateModule();
                m_CurrentInputModule = null;
            }

            //从m_EventSystems中移除
            m_EventSystems.Remove(this);

            base.OnDisable();
        }

        private void TickModules()
        {
            for (var i = 0; i < m_SystemInputModules.Count; i++)
            {
                if (m_SystemInputModules[i] != null)
                    m_SystemInputModules[i].UpdateModule();
            }
        }

        protected virtual void OnApplicationFocus(bool hasFocus)
        {
            m_HasFocus = hasFocus;
        }

        protected virtual void Update()
        {
            if (current != this)
                return;
            TickModules();//更新输入模块的状态，鼠标的位置

            bool changedModule = false;
            for (var i = 0; i < m_SystemInputModules.Count; i++)
            {
                var module = m_SystemInputModules[i];
                //判断module是否支持当前平台IsModuleSupported，并且是否可激活ShouldActivateModule
                if (module.IsModuleSupported() && module.ShouldActivateModule())
                {
                    if (m_CurrentInputModule != module)
                    {
                        //设置当前module
                        ChangeEventModule(module);
                        changedModule = true;
                    }
                    break;
                }
            }

            // no event module set... set the first valid one...
            if (m_CurrentInputModule == null)
            {
                for (var i = 0; i < m_SystemInputModules.Count; i++)
                {
                    var module = m_SystemInputModules[i];
                    if (module.IsModuleSupported())
                    {
                        //判断module是否支持当前平台,设置给m_CurrentInputModule
                        ChangeEventModule(module);
                        changedModule = true;
                        break;
                    }
                }
            }

            //如果m_CurrentInputModule不为空，调用每一个InputModule的Process方法发送事件
            if (!changedModule && m_CurrentInputModule != null)
                m_CurrentInputModule.Process();
        }

        /// <summary>
        /// 调用上一个module的DeactivateModule和当前module的ActivateModule
        /// </summary>
        private void ChangeEventModule(BaseInputModule module)
        {
            if (m_CurrentInputModule == module)
                return;

            if (m_CurrentInputModule != null)
                m_CurrentInputModule.DeactivateModule();

            if (module != null)
                module.ActivateModule();
            m_CurrentInputModule = module;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<b>Selected:</b>" + currentSelectedGameObject);
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine(m_CurrentInputModule != null ? m_CurrentInputModule.ToString() : "No module");
            return sb.ToString();
        }
    }
}
