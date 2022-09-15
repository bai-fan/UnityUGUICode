using System;
using System.Text;
using UnityEngine.Serialization;

namespace UnityEngine.EventSystems
{
    [Obsolete("TouchInputModule is no longer required as Touch input is now handled in StandaloneInputModule.")]
    [AddComponentMenu("Event/Touch Input Module")]
    /// <summary>
    /// 已经弃用
    /// </summary>
    public class TouchInputModule : PointerInputModule
    {
        protected TouchInputModule()
        {}

        private Vector2 m_LastMousePosition;
        private Vector2 m_MousePosition;

        private PointerEventData m_InputPointerEvent;

        [SerializeField]
        [FormerlySerializedAs("m_AllowActivationOnStandalone")]
        private bool m_ForceModuleActive;

        [Obsolete("allowActivationOnStandalone has been deprecated. Use forceModuleActive instead (UnityUpgradable) -> forceModuleActive")]
        public bool allowActivationOnStandalone
        {
            get { return m_ForceModuleActive; }
            set { m_ForceModuleActive = value; }
        }

        public bool forceModuleActive
        {
            get { return m_ForceModuleActive; }
            set { m_ForceModuleActive = value; }
        }

        public override void UpdateModule()
        {
            if (!eventSystem.isFocused)
            {
                if (m_InputPointerEvent != null && m_InputPointerEvent.pointerDrag != null && m_InputPointerEvent.dragging)
                    ExecuteEvents.Execute(m_InputPointerEvent.pointerDrag, m_InputPointerEvent, ExecuteEvents.endDragHandler);

                m_InputPointerEvent = null;
            }

            m_LastMousePosition = m_MousePosition;
            m_MousePosition = input.mousePosition;
        }

        public override bool IsModuleSupported()
        {
            return forceModuleActive || input.touchSupported;
        }

        public override bool ShouldActivateModule()
        {
            if (!base.ShouldActivateModule())
                return false;

            if (m_ForceModuleActive)
                return true;

            if (UseFakeInput())
            {
                bool wantsEnable = input.GetMouseButtonDown(0);

                wantsEnable |= (m_MousePosition - m_LastMousePosition).sqrMagnitude > 0.0f;
                return wantsEnable;
            }

            if (input.touchCount > 0)
                return true;

            return false;
        }

        private bool UseFakeInput()
        {
            return !input.touchSupported;
        }

        public override void Process()
        {
            if (UseFakeInput())
                FakeTouches();
            else
                ProcessTouchEvents();//处理触摸
        }

        /// <summary>
        /// For debugging touch-based devices using the mouse.
        /// 用于使用鼠标调试触摸的设备
        /// </summary>
        private void FakeTouches()
        {
            var pointerData = GetMousePointerEventData(0);

            var leftPressData = pointerData.GetButtonState(PointerEventData.InputButton.Left).eventData;

            // fake touches... on press clear delta
            if (leftPressData.PressedThisFrame())
                leftPressData.buttonData.delta = Vector2.zero;

            ProcessTouchPress(leftPressData.buttonData, leftPressData.PressedThisFrame(), leftPressData.ReleasedThisFrame());

            // only process move if we are pressed...
            if (input.GetMouseButton(0))
            {
                ProcessMove(leftPressData.buttonData);
                ProcessDrag(leftPressData.buttonData);
            }
        }

        /// <summary>
        /// Process all touch events.
        ///  处理所有触屏事件
        /// </summary>
        private void ProcessTouchEvents()
        {
            //支持多点触控，分别处理多个触摸
            for (int i = 0; i < input.touchCount; ++i)
            {
                Touch touch = input.GetTouch(i);

                //只处理直接来自设备的触摸(TouchType.Direct)和来自触摸笔的触摸(TouchType.Stylus)
                if (touch.type == TouchType.Indirect)
                    continue;

                bool released;
                bool pressed;
                //检测被触摸的对象
                var pointer = GetTouchPointerEventData(touch, out pressed, out released);

                //处理触摸按下
                ProcessTouchPress(pointer, pressed, released);

                //没有抬起时，处理移动和拖拽事件
                if (!released)
                {
                    ProcessMove(pointer);
                    ProcessDrag(pointer);
                }
                else//移除触摸事件
                    RemovePointerData(pointer);
            }
        }

        protected void ProcessTouchPress(PointerEventData pointerEvent, bool pressed, bool released)
        {
            var currentOverGo = pointerEvent.pointerCurrentRaycast.gameObject;

            // PointerDown notification
            //按下事件
            if (pressed)
            {
                //赋值用于按下事件的各种参数
                pointerEvent.eligibleForClick = true;
                pointerEvent.delta = Vector2.zero;
                pointerEvent.dragging = false;
                pointerEvent.useDragThreshold = true;
                pointerEvent.pressPosition = pointerEvent.position;
                pointerEvent.pointerPressRaycast = pointerEvent.pointerCurrentRaycast;

                //选中按下的对象
                DeselectIfSelectionChanged(currentOverGo, pointerEvent);

                if (pointerEvent.pointerEnter != currentOverGo)
                {
                    // send a pointer enter to the touched element if it isn't the one to select...
                    //分发进入事件和设置进入对象
                    HandlePointerExitAndEnter(pointerEvent, currentOverGo);
                    pointerEvent.pointerEnter = currentOverGo;
                }

                // search for the control that will receive the press
                // if we can't find a press handler set the press
                // handler to be what would receive a click.
                //在当前对象和其所有的父级对象上查找pointerDown事件触发器
                var newPressed = ExecuteEvents.ExecuteHierarchy(currentOverGo, pointerEvent, ExecuteEvents.pointerDownHandler);

                // didnt find a press handler... search for a click handler
                //如果没有找到则使用当前对象身上的PointerClick
                if (newPressed == null)
                    newPressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

                // Debug.Log("Pressed: " + newPressed);

                float time = Time.unscaledTime;

                if (newPressed == pointerEvent.lastPress)
                {
                    //上次和本次的点击对象相同，记录点击次数和事件，如果两次点击间隔小于0.3f，则增加点击次数
                    var diffTime = time - pointerEvent.clickTime;
                    if (diffTime < 0.3f)
                        ++pointerEvent.clickCount;
                    else
                        pointerEvent.clickCount = 1;

                    pointerEvent.clickTime = time;
                }
                else
                {
                    //否则初始化点击次数为1
                    pointerEvent.clickCount = 1;
                }

                //记录按下对象
                pointerEvent.pointerPress = newPressed;
                //记录原始对象
                pointerEvent.rawPointerPress = currentOverGo;

                pointerEvent.clickTime = time;

                // Save the drag handler as well
                //记录pointerDrag对象
                pointerEvent.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(currentOverGo);

                //向pointerDrag对象发送initializePotentialDrag事件
                if (pointerEvent.pointerDrag != null)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.initializePotentialDrag);

                m_InputPointerEvent = pointerEvent;
            }

            // PointerUp notification
            //抬起
            if (released)
            {
                // Debug.Log("Executing pressup on: " + pointer.pointerPress);
                //向按下对象发送抬起事件
                ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);

                // Debug.Log("KeyCode: " + pointer.eventData.keyCode);

                // see if we mouse up on the same element that we clicked on...
                //获取按下对象
                var pointerUpHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

                // PointerClick and Drop events
                if (pointerEvent.pointerPress == pointerUpHandler && pointerEvent.eligibleForClick)
                {
                    //如果是同一个对象且对象标记了点击，则分发点击事件
                    ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerClickHandler);
                }
                else if (pointerEvent.pointerDrag != null && pointerEvent.dragging)
                {
                    //拖拽过程中抬起
                    ExecuteEvents.ExecuteHierarchy(currentOverGo, pointerEvent, ExecuteEvents.dropHandler);
                }

                //清空按下数据和状态
                pointerEvent.eligibleForClick = false;
                pointerEvent.pointerPress = null;
                pointerEvent.rawPointerPress = null;

                //拖拽结束事件
                if (pointerEvent.pointerDrag != null && pointerEvent.dragging)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.endDragHandler);

                //清空拖拽数据和状态
                pointerEvent.dragging = false;
                pointerEvent.pointerDrag = null;

                // send exit events as we need to simulate this on touch up on touch device
                //发送pointerExit事件
                ExecuteEvents.ExecuteHierarchy(pointerEvent.pointerEnter, pointerEvent, ExecuteEvents.pointerExitHandler);
                pointerEvent.pointerEnter = null;

                m_InputPointerEvent = pointerEvent;
            }
        }

        public override void DeactivateModule()
        {
            base.DeactivateModule();
            ClearSelection();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(UseFakeInput() ? "Input: Faked" : "Input: Touch");
            if (UseFakeInput())
            {
                var pointerData = GetLastPointerEventData(kMouseLeftId);
                if (pointerData != null)
                    sb.AppendLine(pointerData.ToString());
            }
            else
            {
                foreach (var pointerEventData in m_PointerData)
                    sb.AppendLine(pointerEventData.ToString());
            }
            return sb.ToString();
        }
    }
}
