using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;

// 네임스페이스는 현재 프로젝트 이름에 맞게 유지해주세요. (예: UI_26_03_12)
namespace ElevatorHMI
{
    public partial class MainWindow : Window
    {
        // ── 서비스 & 타이머 ──
        private readonly PlcService _plc = new PlcService();
        private DispatcherTimer _pollTimer;
        private DispatcherTimer _keepAliveTimer;
        private bool _emergencyLatched = false;

        // ── UI 배열 ──
        private Ellipse[] _qUp;
        private Ellipse[] _qDn;
        private Ellipse[] _qInt;
        private Button[] _hallBtns;
        private Button[] _carBtns;

        // ── 이동 & 애니메이션 상태 ──
        private double _currentCarY = 0;     // 현재 엘리베이터 위치(Y)
        private double _targetCarY = 0;      // 엘리베이터가 가야 할 목표 위치(Y)

        // 자동 문 개폐 및 상태 동기화를 위한 변수
        private bool _isCarArrived = true;   // UI상 엘리베이터가 완벽히 도착했는지 여부
        private bool _isMoving = false;
        private bool _autoDoorActive = false;

        // ── 색상 상수 ──
        private static readonly SolidColorBrush BrushLedOff = new SolidColorBrush(Color.FromRgb(0xDF, 0xE6, 0xEF));
        private static readonly SolidColorBrush BrushLedUp = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        private static readonly SolidColorBrush BrushLedDn = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush BrushLedInt = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private static readonly SolidColorBrush BrushHallBtnNormal = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush BrushHallBtnActive = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        private static readonly SolidColorBrush BrushCarBtnNormal = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush BrushCarBtnActive = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));

        // ═══════════════════════════════════════════
        //  초기화
        // ═══════════════════════════════════════════
        public MainWindow()
        {
            InitializeComponent();
            InitializeUIArrays();
            InitializeTimers();
        }

        private void InitializeUIArrays()
        {
            _qUp = new Ellipse[] { qUp1, qUp2, qUp3, qUp4, qUp5 };
            _qDn = new Ellipse[] { qDn1, qDn2, qDn3, qDn4, qDn5 };
            _qInt = new Ellipse[] { qInt1, qInt2, qInt3, qInt4, qInt5 };

            _hallBtns = new Button[] { btnHall1Up, btnHall2Up, btnHall2Dn, btnHall3Up, btnHall3Dn, btnHall4Up, btnHall4Dn, btnHall5Dn };
            _carBtns = new Button[] { btnCar1, btnCar2, btnCar3, btnCar4, btnCar5 };
        }

        private void InitializeTimers()
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _pollTimer.Tick += PollTimer_Tick;

            _keepAliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _keepAliveTimer.Tick += KeepAlive_Tick;
        }

        // ═══════════════════════════════════════════
        //  접속 / 해제
        // ═══════════════════════════════════════════
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            int port = 2004; // 내부 고정 포트
            AddLog("접속 시도: {0}:{1}", txtIP.Text.Trim(), port);

            if (_plc.Connect(txtIP.Text.Trim(), port))
            {
                AddLog("PLC 접속 성공!");
                SetConnectionUI(true);
                UpdateCarPosition(true); // 접속 시 현재 위치로 즉시 스냅
                _pollTimer.Start();
                _keepAliveTimer.Start();
            }
            else
            {
                AddLog("PLC 접속 실패: {0}", _plc.LastConnectError ?? "원인불명");
                SetConnectionUI(false);
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _pollTimer.Stop();
            _keepAliveTimer.Stop();
            _plc.Disconnect();
            SetConnectionUI(false);
            AddLog("PLC 연결 해제");
        }

        private void SetConnectionUI(bool connected)
        {
            btnConnect.IsEnabled = !connected;
            btnDisconnect.IsEnabled = connected;
            txtIP.IsEnabled = !connected;
            valComm.Text = connected ? "연결됨" : "미연결";
            valComm.Foreground = connected ? Brushes.Green : new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _pollTimer.Stop();
            _keepAliveTimer.Stop();
            try { _plc.Disconnect(); } catch { }
        }

        // ═══════════════════════════════════════════
        //  타이머 콜백
        // ═══════════════════════════════════════════
        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (_isMoving) return; // 이동 중이면 폴링 스킵
            if (!_plc.PollAll())
            {
                SetConnectionUI(false);
                _pollTimer.Stop();
                _keepAliveTimer.Stop();
                AddLog("통신 에러 — 연결 끊김");
                return;
            }
            UpdateBuilding();
            UpdateStatus();
            UpdateQueues();
            UpdateButtonHighlights();
        }

        private void KeepAlive_Tick(object sender, EventArgs e)
        {
            _plc.KeepAlive();
        }

        // ═══════════════════════════════════════════
        //  UI 갱신 (부드러운 이동 & 애니메이션)
        // ═══════════════════════════════════════════
        private void UpdateBuilding()
        {
            UpdateCarPosition(false);
            UpdateDoorVisual();
        }

        private void ShaftCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCarPosition(true); // 크기 변경 시 위치 재조정
            UpdateDoorVisual();
        }
        private void UpdateCarPosition(bool snap)
        {
            int floor = _plc.CurrentFloor > 0 ? _plc.CurrentFloor : 1;
            double canvasHeight = ShaftCanvas.ActualHeight;
            double canvasWidth = ShaftCanvas.ActualWidth;
            if (canvasHeight <= 0 || canvasWidth <= 0) return;

            double carHeight = 70.0;
            double carWidth = 66.0;
            double cellHeight = canvasHeight / 5.0;
            int index = 5 - floor;

            double newTargetY = index * cellHeight + cellHeight / 2.0 - carHeight / 2.0;
            double targetX = canvasWidth / 2.0 - carWidth / 2.0;
            Canvas.SetLeft(MainCar, targetX);

            if (snap)
            {
                MainCar.BeginAnimation(Canvas.TopProperty, null); // 진행 중인 애니메이션 즉시 중단
                Canvas.SetTop(MainCar, newTargetY);
                _currentCarY = newTargetY;
                _targetCarY = newTargetY;
                _isCarArrived = true;
                _isMoving = false;
                return;
            }

            if (Math.Abs(_targetCarY - newTargetY) < 1.0) return; // 목표층 변경 없으면 무시

            _targetCarY = newTargetY;
            _isCarArrived = false;
            _isMoving = true;

            var anim = new DoubleAnimation(_targetCarY, TimeSpan.FromMilliseconds(1000))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (s, e) =>
            {
                _currentCarY = _targetCarY;
                _isCarArrived = true;
                _isMoving = false;
                TriggerAutoDoor();
            };
            MainCar.BeginAnimation(Canvas.TopProperty, anim);
        }
        //private void UpdateCarPosition(bool snap)
        //{
        //    int floor = _plc.CurrentFloor > 0 ? _plc.CurrentFloor : 1;
        //    double canvasHeight = ShaftCanvas.ActualHeight;
        //    double canvasWidth = ShaftCanvas.ActualWidth;

        //    if (canvasHeight <= 0 || canvasWidth <= 0) return;

        //    // 엘리베이터 카의 명시적 고정 크기
        //    double carHeight = 70.0;
        //    double carWidth = 66.0;

        //    // 5등분하여 각 층의 셀(칸) 높이 계산
        //    double cellHeight = canvasHeight / 5.0;

        //    // 5층=인덱스0(맨 위), 1층=인덱스4(맨 아래)
        //    int index = 5 - floor;

        //    // 목표 좌표 계산
        //    _targetCarY = (index * cellHeight) + (cellHeight / 2.0) - (carHeight / 2.0);

        //    if (snap)
        //    {
        //        _currentCarY = _targetCarY;
        //    }
        //    else
        //    {
        //        // 부드러운 보간 (Lerp) 이동
        //        _currentCarY += (_targetCarY - _currentCarY) * 0.15;
        //    }

        //    // UI 엘리베이터가 화면상 목표 좌표에 완벽히 도착했는지 갱신
        //    _isCarArrived = Math.Abs(_currentCarY - _targetCarY) < 1.0;

        //    // Y축 이동
        //    Canvas.SetTop(MainCar, _currentCarY);
        //    Canvas.SetLeft(MainCar, (canvasWidth / 2.0) - (carWidth / 2.0));

        //    // ==========================================
        //    // 자동 문 개폐 로직
        //    // ==========================================
        //    if (!_isCarArrived)
        //    {
        //        _isMoving = true; // 이동 중
        //    }
        //    else if (_isMoving)
        //    {
        //        _isMoving = false; // 방금 완벽히 정지함!
        //        TriggerAutoDoor(); // 자동 문 열림/닫힘 시퀀스 시작
        //    }
        //}

        // 도착 시 자동으로 문을 열고 닫는 시퀀스
        private async void TriggerAutoDoor()
        {
            if (!_plc.IsConnected || _autoDoorActive) return;

            _autoDoorActive = true;

            try
            {
                // 1. 도착 직후 0.5초간 멈춤 유지 (자연스러운 연출)
                await System.Threading.Tasks.Task.Delay(500);

                // 2. 문 열기 명령 전송 (MB31.5)
                _plc.WriteCarCall(5, true);
                await System.Threading.Tasks.Task.Delay(500);
                _plc.WriteCarCall(5, false);

                // 3. 3초 동안 문을 열어둠 (승객 탑승 시간)
                await System.Threading.Tasks.Task.Delay(3000);

                // 4. 문 닫기 명령 전송 (MB31.6)
                _plc.WriteCarCall(6, true);
                await System.Threading.Tasks.Task.Delay(300);
                _plc.WriteCarCall(6, false);
            }
            finally
            {
                _autoDoorActive = false;
            }
        }
        private bool _doorIsOpen = false;

        private void UpdateDoorVisual()
        {
            // 이동 중이면 문 강제 닫힘
            bool shouldOpen = _isCarArrived && (_plc.IsDoorOpen || _plc.IsDoorOpening);

            if (shouldOpen == _doorIsOpen) return; // 상태 변화 없으면 무시
            _doorIsOpen = shouldOpen;

            double hostWidth = doorHost.ActualWidth;
            if (hostWidth <= 0) hostWidth = 60;

            double targetWidth = shouldOpen ? 0.0 : hostWidth / 2.0;
            var dur = TimeSpan.FromMilliseconds(shouldOpen ? 700 : 500);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            doorLeft.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(targetWidth, dur) { EasingFunction = ease });
            doorRight.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(targetWidth, dur) { EasingFunction = ease });
        }
        //private void UpdateDoorVisual()
        //{
        //    double targetOpenRatio = 0.0;

        //    // 시각적으로 엘리베이터가 도착했을 때만 문의 개폐를 허용
        //    if (_isCarArrived)
        //    {
        //        targetOpenRatio = (_plc.IsDoorOpen || _plc.IsDoorOpening) ? 1.0 : 0.0;
        //    }

        //    const double step = 0.2; // 애니메이션 속도

        //    if (_doorProgress < targetOpenRatio)
        //        _doorProgress = Math.Min(targetOpenRatio, _doorProgress + step);
        //    else if (_doorProgress > targetOpenRatio)
        //        _doorProgress = Math.Max(targetOpenRatio, _doorProgress - step);

        //    double hostWidth = doorHost.ActualWidth;
        //    if (hostWidth <= 0) hostWidth = 60;

        //    double panelWidth = Math.Max(0.0, (hostWidth / 2.0) * (1.0 - _doorProgress));

        //    doorLeft.Width = panelWidth;
        //    doorRight.Width = panelWidth;
        //}

        private void UpdateStatus()
        {
            valFloor.Text = _plc.CurrentFloor > 0 ? _plc.CurrentFloor + "F" : "-";
            valTarget.Text = _plc.TargetFloor > 0 ? _plc.TargetFloor + "F" : "-";

            string[] stateNames = { "IDLE", "상승 중", "하강 중", "문 열리는 중", "문 열림", "문 닫히는 중", "비상정지" };
            int st = _plc.State;

            // 방향 표시
            switch (_plc.Direction)
            {
                case 1: valDir.Text = "▲ 상승"; valDir.Foreground = Brushes.Blue; break;
                case 2: valDir.Text = "▼ 하강"; valDir.Foreground = Brushes.Red; break;
                default: valDir.Text = "-"; valDir.Foreground = Brushes.Gray; break;
            }

            // 🌟 강제 덮어쓰기 로직: UI 상 엘리베이터가 아직 이동 중일 때
            if (!_isCarArrived)
            {
                // 운행 상태 강제 고정
                if (_targetCarY < _currentCarY - 1.0)
                {
                    valState.Text = "상승 중";
                    valState.Foreground = Brushes.Blue;
                }
                else if (_targetCarY > _currentCarY + 1.0)
                {
                    valState.Text = "하강 중";
                    valState.Foreground = Brushes.Red;
                }
                else
                {
                    valState.Text = "이동 중";
                    valState.Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x31, 0x4F));
                }

                // 문 상태 강제 고정
                valDoor.Text = "닫힘";
                valDoor.Foreground = Brushes.Gray;

                // LCD 상태 강제 고정
                txtLcdStatus.Text = "STATUS: DOOR CLOSED";
            }
            else
            {
                // 도착 완료 후에는 정상적으로 PLC 상태를 반영
                valState.Text = (st >= 0 && st < stateNames.Length) ? stateNames[st] : "IDLE";
                valState.Foreground = (st == 6) ? Brushes.Red : new SolidColorBrush(Color.FromRgb(0x17, 0x31, 0x4F));

                if (_plc.IsDoorOpen) { valDoor.Text = "열림"; valDoor.Foreground = Brushes.Green; }
                else if (_plc.IsDoorOpening) { valDoor.Text = "열리는 중"; valDoor.Foreground = Brushes.Orange; }
                else if (_plc.IsDoorClosing) { valDoor.Text = "닫히는 중"; valDoor.Foreground = Brushes.Orange; }
                else if (_plc.IsDoorClosed) { valDoor.Text = "닫힘"; valDoor.Foreground = Brushes.Gray; }
                else { valDoor.Text = "⚠에러"; valDoor.Foreground = Brushes.Red; }

                string lcdStatus = "DOOR CLOSED";
                if (_plc.IsDoorOpen) lcdStatus = "DOOR OPEN";
                else if (_plc.IsDoorOpening) lcdStatus = "DOOR OPENING...";
                else if (_plc.IsDoorClosing) lcdStatus = "DOOR CLOSING...";
                txtLcdStatus.Text = "STATUS: " + lcdStatus;
            }

            // 🚨 비상정지는 이동 중이어도 무조건 최우선 반영
            if (_plc.IsEmergency)
            {
                valState.Text = "비상정지";
                valState.Foreground = Brushes.Red;
                valDoor.Text = "비상정지";
                valDoor.Foreground = Brushes.Red;
                txtLcdStatus.Text = "STATUS: EMERGENCY STOP";
            }

            valEmergency.Text = _plc.IsEmergency ? "비상정지!" : "정상";
            valEmergency.Foreground = _plc.IsEmergency ? Brushes.Red : Brushes.Green;

            txtLcdFloor.Text = _plc.CurrentFloor > 0 ? _plc.CurrentFloor + "F" : "-";

            txtExternalStatus.Text = _plc.IsEmergency ? "SYSTEM ERROR" : "SYSTEM NORMAL";
            borderExternalStatus.Background = _plc.IsEmergency ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0xD2)) : new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9));
            txtExternalStatus.Foreground = _plc.IsEmergency ? new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)) : new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C));

            _emergencyLatched = _plc.IsEmergency;
        }

        private void UpdateQueues()
        {
            byte up = _plc.UpCallQueue;
            byte dn = _plc.DownCallQueue;
            byte it = _plc.InternalQueue;

            for (int i = 0; i < 5; i++)
            {
                _qUp[i].Fill = (up & (1 << i)) != 0 ? BrushLedUp : BrushLedOff;
                _qDn[i].Fill = (dn & (1 << i)) != 0 ? BrushLedDn : BrushLedOff;
                _qInt[i].Fill = (it & (1 << i)) != 0 ? BrushLedInt : BrushLedOff;
            }
        }

        private void UpdateButtonHighlights()
        {
            byte up = _plc.UpCallQueue;
            byte dn = _plc.DownCallQueue;
            byte it = _plc.InternalQueue;

            btnHall1Up.Background = (up & 0x01) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall2Up.Background = (up & 0x02) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall2Dn.Background = (dn & 0x02) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall3Up.Background = (up & 0x04) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall3Dn.Background = (dn & 0x04) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall4Up.Background = (up & 0x08) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall4Dn.Background = (dn & 0x08) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;
            btnHall5Dn.Background = (dn & 0x10) != 0 ? BrushHallBtnActive : BrushHallBtnNormal;

            for (int i = 0; i < 5; i++)
                _carBtns[i].Background = (it & (1 << i)) != 0 ? BrushCarBtnActive : BrushCarBtnNormal;
        }

        // ═══════════════════════════════════════════
        //  버튼 클릭 → PLC 쓰기
        // ═══════════════════════════════════════════
        private async void HallCall_Click(object sender, RoutedEventArgs e)
        {
            if (!_plc.IsConnected) return;
            int bit = int.Parse(((Button)sender).Tag.ToString());
            _plc.WriteHallCall(bit, true);
            await System.Threading.Tasks.Task.Delay(300);
            _plc.WriteHallCall(bit, false);
        }

        private async void CarCall_Click(object sender, RoutedEventArgs e)
        {
            if (!_plc.IsConnected) return;
            int bit = int.Parse(((Button)sender).Tag.ToString());
            _plc.WriteCarCall(bit, true);
            await System.Threading.Tasks.Task.Delay(300);
            _plc.WriteCarCall(bit, false);
        }

        private async void DoorOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!_plc.IsConnected) return;
            _plc.WriteCarCall(5, true);
            await System.Threading.Tasks.Task.Delay(500);
            _plc.WriteCarCall(5, false);
        }

        private async void DoorClose_Click(object sender, RoutedEventArgs e)
        {
            if (!_plc.IsConnected) return;
            _plc.WriteCarCall(6, true);
            await System.Threading.Tasks.Task.Delay(300);
            _plc.WriteCarCall(6, false);
        }

        private void Emergency_Click(object sender, RoutedEventArgs e)
        {
            if (!_plc.IsConnected) return;
            _emergencyLatched = !_emergencyLatched;
            _plc.WriteCarCall(7, _emergencyLatched);
        }

        private void AddLog(string msg, params object[] args)
        {
            string text = (args.Length > 0) ? string.Format(msg, args) : msg;
            string line = DateTime.Now.ToString("[HH:mm:ss.fff] ") + text;
            System.Diagnostics.Debug.WriteLine(line);
        }
    }
}