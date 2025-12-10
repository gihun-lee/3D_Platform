# Visual Programming Platform - 3D Point Cloud Inspection System

> .NET 8 기반의 노드형 비주얼 프로그래밍 플랫폼으로 3D 포인트 클라우드 처리, 원형 검출, 합격/불합격 검사를 위한 산업용 솔루션

---

## 📋 목차

- [프로젝트 개요](#-프로젝트-개요)
- [핵심 아키텍처](#-핵심-아키텍처)
- [렌더링 시스템](#-렌더링-시스템)
- [디자인 패턴](#-디자인-패턴)
- [데이터 플로우](#-데이터-플로우)
- [프로젝트 구조](#-프로젝트-구조)
- [노드 시스템](#-노드-시스템)
- [주요 기능](#-주요-기능)
- [알고리즘 상세](#-알고리즘-상세)
- [기술 스택](#-기술-스택)
- [사용 방법](#-사용-방법)
- [확장 개발](#-확장-개발)

---

## 🎯 프로젝트 개요

### 목적
이 플랫폼은 **3D 포인트 클라우드 데이터**를 시각적으로 처리하고 검사하기 위한 노드 기반 프로그래밍 환경입니다. 주로 산업용 품질 검사, 원형(Circle) 형상 검출, 측정값 검증 등에 사용됩니다.

### 핵심 개념
- **노드 기반 워크플로우**: 드래그 앤 드롭으로 데이터 처리 파이프라인 구성
- **GPU 가속 시각화**: 수백만 개의 포인트를 실시간으로 렌더링
- **플러그인 아키텍처**: 새로운 처리 노드를 쉽게 추가할 수 있는 확장 가능한 구조
- **컨텍스트 기반 데이터 공유**: 노드 간 암묵적 데이터 전달로 유연한 워크플로우 구성

### 주요 사용 시나리오
1. **제조업 품질 검사**: 가공된 부품의 원형 구멍, 구멍 위치, 크기 검증
2. **3D 스캔 데이터 분석**: 포인트 클라우드 파일 로드 및 ROI(관심 영역) 기반 필터링
3. **자동화된 검사 워크플로우**: 워크플로우를 JSON으로 저장/로드하여 반복 작업 자동화

---

## 🏛 핵심 아키텍처

### 전체 시스템 구조

```
┌─────────────────────────────────────────────────────────────────┐
│                        WPF Presentation Layer                    │
│  ┌──────────────────┐  ┌─────────────────┐  ┌────────────────┐ │
│  │  MainWindow      │  │  3D Viewport    │  │  Node Editor   │ │
│  │  (XAML View)     │◄─┤  (HelixToolkit) │  │  Canvas        │ │
│  └────────┬─────────┘  └────────┬────────┘  └───────┬────────┘ │
└───────────┼─────────────────────┼────────────────────┼──────────┘
            │                     │                    │
            │ Data Binding        │ Rendering          │ Commands
            ▼                     ▼                    ▼
┌─────────────────────────────────────────────────────────────────┐
│                     ViewModel Layer (MVVM)                       │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              MainViewModel                                │   │
│  │  • RelayCommands (ExecuteGraph, LoadPointCloud, etc.)   │   │
│  │  • ObservableCollections (Nodes, Connections)           │   │
│  │  • Scene3D Management (Camera, Lights, Geometries)      │   │
│  └──────────┬──────────────────────────────┬────────────────┘   │
└─────────────┼──────────────────────────────┼────────────────────┘
              │                              │
              │ Commands                     │ Updates
              ▼                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Core Engine Layer                           │
│  ┌────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │ ExecutionEngine│  │  NodeGraph      │  │  PluginService  │  │
│  │ • Topological  │  │  • Nodes List   │  │  • Discovery    │  │
│  │   Sort         │  │  • Connections  │  │  • Registration │  │
│  │ • Sequential   │  │  • Cycle Check  │  │  • Factory      │  │
│  │   Execution    │  └─────────────────┘  └─────────────────┘  │
│  └────────┬───────┘                                             │
│           │                                                      │
│           ▼                                                      │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │            ExecutionContext (Shared State)               │   │
│  │  Dictionary<string, object>                             │   │
│  │  • "PointCloud_{NodeId}" → PointCloudData              │   │
│  │  • "FilteredCloud" → Filtered points                   │   │
│  │  • "CircleResult" → CircleDetectionResult              │   │
│  │  • "InspectionResult" → Pass/Fail status               │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────┬───────────────────────────┘
                                      │
                                      │ Node Execution
                                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Plugin Layer (Nodes)                        │
│  ┌──────────────┐  ┌───────────────┐  ┌───────────────────┐    │
│  │ Import Node  │  │ ROI Filter    │  │ Circle Detection  │    │
│  │ • PLY/PCD    │  │ • Box/Cylinder│  │ • RANSAC          │    │
│  │ • XYZ/CSV    │  │ • Sphere      │  │ • Plane Project   │    │
│  └──────────────┘  └───────────────┘  └───────────────────┘    │
│  ┌──────────────┐  ┌───────────────┐                            │
│  │ Inspection   │  │ Transform     │                            │
│  │ • Tolerance  │  │ • Rigid       │                            │
│  │ • Pass/Fail  │  │ • Multi-Cloud │                            │
│  └──────────────┘  └───────────────┘                            │
└─────────────────────────────────────────────────────────────────┘
```

### 레이어별 책임

#### 1. **Presentation Layer (WPF/XAML)**
- **책임**: 사용자 인터페이스, 이벤트 처리, 데이터 바인딩
- **기술**: WPF, XAML, CommunityToolkit.Mvvm
- **주요 컴포넌트**:
  - `MainWindow.xaml`: 메인 UI (3D 뷰포트, 노드 에디터, 상태바)
  - `Converters`: XAML 값 변환기 (Bool to Visibility 등)
  - `Resources`: 스타일, 템플릿, 리소스 딕셔너리

#### 2. **ViewModel Layer (MVVM Pattern)**
- **책임**: UI 로직, 명령 처리, 상태 관리
- **기술**: CommunityToolkit.Mvvm (RelayCommand, ObservableObject)
- **주요 컴포넌트**:
  - `MainViewModel`: 애플리케이션의 중심 뷰모델
  - Commands: ExecuteGraphCommand, LoadPointCloudCommand 등
  - ObservableCollections: 노드, 연결, 3D 지오메트리

#### 3. **Core Engine Layer**
- **책임**: 그래프 실행, 플러그인 관리, 데이터 흐름 제어
- **기술**: .NET 8, System.Composition (MEF)
- **주요 컴포넌트**:
  - `ExecutionEngine`: 그래프 토폴로지 정렬 및 순차 실행
  - `NodeGraph`: 노드와 연결 관리, 순환 참조 검출
  - `PluginService`: 리플렉션 기반 플러그인 발견 및 로드
  - `ExecutionContext`: 노드 간 데이터 공유를 위한 컨텍스트

#### 4. **Plugin Layer (Nodes)**
- **책임**: 구체적인 데이터 처리 로직 구현
- **기술**: 커스텀 노드 구현, 알고리즘 (RANSAC, 필터링)
- **주요 컴포넌트**:
  - `NodeBase`: 모든 노드의 추상 베이스 클래스
  - Point Cloud Nodes: Import, ROI, Filter, Detection, Inspection
  - `PointCloudPlugin`: 노드 등록 및 플러그인 메타데이터

---

## 🎨 렌더링 시스템

### GPU 가속 렌더링

이 프로젝트는 **HelixToolkit.Wpf.SharpDX**를 사용하여 **GPU 기반 3D 렌더링**을 수행합니다.

#### 렌더링 파이프라인

```
Point Cloud Data (CPU)
        ↓
Memory Buffer Allocation
        ↓
Vertex Buffer Creation (SharpDX)
        ↓
GPU Memory Upload (DirectX)
        ↓
Shader Execution (GPU)
        ↓
Rasterization & Display
```

#### 렌더링 기술 스택

| 레이어 | 기술 | 역할 |
|-------|------|------|
| **렌더링 라이브러리** | HelixToolkit.Wpf.SharpDX 2.25.0 | WPF용 고성능 3D 렌더링 툴킷 |
| **그래픽 API 래퍼** | SharpDX 4.2.0 | .NET용 DirectX API 래퍼 |
| **그래픽 API** | DirectX 11 | Windows 네이티브 GPU API |
| **처리 장치** | **GPU (그래픽 카드)** | 하드웨어 가속 렌더링 |

### GpuPointCloudRenderer 상세

**위치**: `/src/VPP.App/Rendering/GpuPointCloudRenderer.cs`

#### 핵심 기능

1. **LOD (Level of Detail) 시스템**
   - 포인트 클라우드 크기에 따라 자동으로 렌더링 품질 조정
   - 메모리 오버플로우 방지 및 프레임레이트 유지

   ```
   포인트 수         LOD 레벨    Stride   렌더링 비율
   ─────────────────────────────────────────────────
   < 2M              Full        1        100%
   2M - 5M           High        1        100%
   5M - 10M          Medium      2        50%
   > 10M             Adaptive    동적     동적
   ```

2. **메모리 효율적 버퍼 관리**
   - Vertex Buffer 재사용으로 할당 최소화
   - Color4 구조체로 GPU 친화적 색상 포맷
   - Stride 기반 포인트 간격 조정

3. **Multi-Cloud 지원**
   - 여러 Import 노드의 포인트 클라우드를 하나의 GPU 버퍼로 병합
   - Transform 체인 지원으로 좌표계 변환

4. **실시간 업데이트**
   - 노드 실행 후 즉시 시각화 업데이트
   - 카메라 자동 피팅 (Bounding Box 기반)

#### 렌더링되는 3D 요소

| 요소 | 지오메트리 타입 | 색상 | 용도 |
|------|----------------|------|------|
| **포인트 클라우드** | PointGeometry3D | 회색 또는 파일 색상 | 원본 3D 데이터 |
| **필터링된 클라우드** | PointGeometry3D | 녹색 | ROI 내부 포인트 |
| **검출된 원 포인트** | PointGeometry3D | 노란색 | 원형 경계 인라이어 |
| **원 외곽선** | LineGeometry3D | 빨간색 | 검출된 2D 원 (3D 투영) |
| **ROI 와이어프레임** | LineGeometry3D | 노란색 | Box/Cylinder/Sphere 경계 |
| **ROI 중심 마커** | MeshGeometry3D (Sphere) | 빨간색 | ROI 중심점 표시 |
| **좌표축** | LineGeometry3D | RGB (XYZ) | 월드 좌표계 원점 |

### 카메라 시스템

- **카메라 타입**: PerspectiveCamera
- **자동 피팅**: 포인트 클라우드의 Bounding Box에 맞춰 자동 조정
- **Far Plane 동적 조정**: 큰 포인트 클라우드도 클리핑 없이 표시
- **마우스 컨트롤**:
  - 좌클릭 드래그: 회전
  - 우클릭 드래그: 팬
  - 휠: 줌

---

## 🧩 디자인 패턴

### 1. MVVM (Model-View-ViewModel) 패턴

#### 구조
```
View (MainWindow.xaml)
    ↕ Data Binding
ViewModel (MainViewModel.cs)
    ↕ Commands & Properties
Model (NodeGraph, PointCloudData, ExecutionContext)
```

#### 이점
- **UI와 로직 완전 분리**: 비즈니스 로직을 UI 없이 테스트 가능
- **양방향 바인딩**: UI 변경이 자동으로 모델에 반영
- **INotifyPropertyChanged**: CommunityToolkit.Mvvm이 자동 구현

#### 구현 예시
```csharp
// ViewModel
[ObservableProperty]
private string _statusMessage;  // Auto-generates StatusMessage property

[RelayCommand]
private async Task ExecuteGraph()  // Auto-generates ExecuteGraphCommand
{
    // Logic here
}
```

### 2. Plugin Architecture (플러그인 아키텍처)

#### 구조
```
IPlugin Interface
    ↑ implements
PointCloudPlugin
    ↓ registers
[NodeInfo] Attributes
    ↓ discovered by
PluginService (Reflection)
    ↓ creates
Node Instances (Factory Pattern)
```

#### 핵심 메커니즘
- **리플렉션 기반 발견**: Assembly 스캔으로 `[NodeInfo]` 속성 탐지
- **지연 로딩**: 노드는 필요할 때만 인스턴스화
- **카테고리 시스템**: 노드를 논리적 그룹으로 분류 (Point Cloud/IO, Point Cloud/ROI 등)

#### 확장성
새로운 플러그인 추가 시:
1. `IPlugin` 인터페이스 구현
2. `NodeBase` 상속한 노드 클래스 작성
3. `[NodeInfo]` 속성으로 메타데이터 제공
4. 플러그인 어셈블리를 애플리케이션과 함께 배포

### 3. Composite Pattern (컴포지트 패턴)

#### 구조
```
NodeGraph (Composite)
    ├── Node 1 (Component)
    ├── Node 2 (Component)
    ├── Node 3 (Component)
    └── Connections (Relations)
```

#### 적용
- **NodeGraph**: 노드들의 컨테이너 (복합 객체)
- **NodeBase**: 개별 노드 (컴포넌트)
- **계층적 실행**: Topological Sort로 의존성 순서대로 실행

### 4. Command Pattern (커맨드 패턴)

#### 구조
```
User Action → RelayCommand → Execute() → Business Logic
                    ↓
                CanExecute() → Enable/Disable UI
```

#### 적용 예시
- `ExecuteGraphCommand`: 그래프 실행
- `LoadPointCloudCommand`: 포인트 클라우드 로드
- `DetectCircleCommand`: 원형 검출 트리거
- `InspectCommand`: 검사 실행

#### 이점
- **Undo/Redo 준비**: 커맨드 히스토리를 쌓아 실행 취소 가능
- **매크로 기능**: 커맨드 시퀀스를 저장하여 재실행
- **UI 상태 관리**: `CanExecute`로 버튼 활성화/비활성화

### 5. Strategy Pattern (전략 패턴)

#### 구조
```
NodeBase (Context)
    ↓ calls
ExecuteCoreAsync() (Strategy Interface)
    ↑ implements
Concrete Nodes (Concrete Strategies)
```

#### 적용
- 각 노드는 고유한 실행 전략(`ExecuteCoreAsync`)을 구현
- 파일 로더: PLY, PCD, XYZ, CSV 각각 다른 파싱 전략
- ROI 필터링: Box, Cylinder, Sphere 각각 다른 필터링 전략

### 6. Observer Pattern (옵저버 패턴)

#### 적용
- **INotifyPropertyChanged**: 프로퍼티 변경 시 UI 자동 업데이트
- **ObservableCollection**: 컬렉션 변경 시 UI 자동 반영
- **이벤트**: `ExecutionEngine.NodeExecuted` → ViewModel → UI 업데이트

---

## 🔄 데이터 플로우

### 전체 데이터 흐름

```
User Interaction (UI)
    ↓
Command Execution (ViewModel)
    ↓
ExecutionEngine.ExecuteAsync()
    ↓
Topological Sort (Dependency Resolution)
    ↓
┌─────────────────────────────────────────┐
│     Sequential Node Execution           │
│                                          │
│  Node 1: Import Point Cloud             │
│    ├─ Load File (PLY/PCD/XYZ/CSV)      │
│    ├─ Parse Data                        │
│    └─ Store: context["PointCloud_{Id}"]│
│              ↓                           │
│  Node 2: ROI Draw                       │
│    ├─ Define 3D Region (Box/Cylinder)  │
│    └─ Store: context["ROI_{Id}"]       │
│              ↓                           │
│  Node 3: ROI Filter                     │
│    ├─ Read: context["PointCloud_{Id}"] │
│    ├─ Read: context["ROI_{Id}"]        │
│    ├─ Filter Points inside ROI         │
│    └─ Store: context["FilteredCloud"]  │
│              ↓                           │
│  Node 4: Circle Detection               │
│    ├─ Read: context["FilteredCloud"]   │
│    ├─ RANSAC Algorithm                  │
│    └─ Store: context["CircleResult"]   │
│              ↓                           │
│  Node 5: Inspection                     │
│    ├─ Read: context["CircleResult"]    │
│    ├─ Validate Tolerances               │
│    └─ Store: context["InspectionResult"]│
└─────────────────────────────────────────┘
    ↓
Update Visualization (GpuPointCloudRenderer)
    ↓
Display Results (3D Viewport + Status Message)
```

### ExecutionContext 데이터 구조

```csharp
ExecutionContext: Dictionary<string, object>
{
    // Point Cloud Data (per node)
    "PointCloud_node1": PointCloudData { Points, Colors, BoundingBox },
    "PointCloud_node2": PointCloudData { ... },

    // ROI Definitions
    "ROI_draw1": ROI3D { Type=Box, Center, Size },
    "ROI_draw2": ROI3D { Type=Cylinder, Center, Radius, Height },

    // Filtered Results
    "FilteredCloud": PointCloudData { ... },

    // Detection Results
    "CircleResult": CircleDetectionResult {
        Center3D,
        Radius,
        Normal,
        Inliers,
        FitError
    },

    // Detection Visualization
    "DetectedCircleCloud": PointCloudData { BoundaryPoints },

    // Inspection Results
    "InspectionResult": InspectionResult {
        Passed: true/false,
        Message: "Pass: All measurements within tolerance",
        Measurements: { Radius, CenterX, CenterY, ... }
    }
}
```

### 데이터 공유 메커니즘

#### 암묵적 컨텍스트 기반 공유
- **이전 방식 (Deprecated)**: 명시적 포트 연결 (`Node1.Output → Node2.Input`)
- **현재 방식**: 컨텍스트 키를 통한 암묵적 공유

#### 장점
1. **유연성**: 노드가 여러 데이터 소스를 동적으로 참조 가능
2. **다중 입력**: 여러 Import 노드의 포인트 클라우드를 동시 처리
3. **선택적 데이터**: 필요한 데이터만 컨텍스트에서 읽음
4. **글로벌 상태**: 모든 노드가 공통 데이터(예: 마지막 검출 결과)에 접근

#### 예시: Multi-Cloud Transform
```csharp
// Transform 노드가 모든 Import 노드의 포인트 클라우드를 자동으로 수집
var allCloudKeys = context.Keys
    .Where(k => k.StartsWith("PointCloud_"))
    .ToList();

foreach (var key in allCloudKeys)
{
    var cloud = context[key] as PointCloudData;
    // Apply transformation...
}
```

### 실행 순서 결정 (Topological Sort)

```
Graph: A → B → C
       A → D → C

Topological Sort Result: [A, B, D, C] or [A, D, B, C]

Execution:
1. A executes (no dependencies)
2. B and D execute (both depend only on A)
3. C executes (depends on B and D)
```

- **알고리즘**: Kahn's Algorithm (BFS 기반)
- **순환 검출**: 연결 추가 시 사이클 체크
- **병렬 가능성**: 독립적인 노드는 병렬 실행 가능 (현재는 순차 구현)

---

## 📁 프로젝트 구조

```
VisualProgrammingPlatform/
│
├── VisualProgrammingPlatform.sln          # 솔루션 파일
├── README.md                               # 이 문서
├── CIRCLE_DETECTION_FEATURE.md            # 원형 검출 상세 문서
│
└── src/
    │
    ├── VPP.Core/                          # 코어 엔진 (플랫폼 독립적)
    │   ├── VPP.Core.csproj
    │   ├── Interfaces/
    │   │   ├── INode.cs                   # 노드 인터페이스
    │   │   ├── IPlugin.cs                 # 플러그인 인터페이스
    │   │   ├── IParameter.cs              # 파라미터 인터페이스
    │   │   └── IPort.cs                   # 포트 인터페이스 (Deprecated)
    │   ├── Models/
    │   │   ├── NodeBase.cs                # 노드 추상 베이스 클래스
    │   │   ├── NodeGraph.cs               # 그래프 모델 (노드 + 연결)
    │   │   ├── Connection.cs              # 노드 간 연결
    │   │   ├── ExecutionContext.cs        # 실행 컨텍스트 (데이터 공유)
    │   │   └── Parameters/                # 파라미터 구현
    │   ├── Engine/
    │   │   └── ExecutionEngine.cs         # 그래프 실행 엔진
    │   ├── Services/
    │   │   ├── PluginService.cs           # 플러그인 로드 및 관리
    │   │   └── GraphSerializer.cs         # 워크플로우 JSON 직렬화
    │   └── Attributes/
    │       └── NodeInfoAttribute.cs       # 노드 메타데이터 속성
    │
    ├── VPP.Plugins.PointCloud/            # 포인트 클라우드 플러그인
    │   ├── VPP.Plugins.PointCloud.csproj
    │   ├── PointCloudPlugin.cs            # 플러그인 등록 클래스
    │   ├── Models/
    │   │   ├── PointCloudData.cs          # 포인트 클라우드 데이터 모델
    │   │   ├── ROI3D.cs                   # 3D ROI 정의
    │   │   ├── CircleDetectionResult.cs   # 원형 검출 결과
    │   │   └── InspectionResult.cs        # 검사 결과
    │   └── Nodes/
    │       ├── ImportPointCloudNode.cs    # 파일 로드 노드
    │       ├── ROIDrawNode.cs             # ROI 정의 노드
    │       ├── ROIFilterNode.cs           # ROI 필터링 노드
    │       ├── CircleDetectionNode.cs     # 원형 검출 노드 (RANSAC)
    │       ├── InspectionNode.cs          # 합격/불합격 검사 노드
    │       └── RigidTransformNode.cs      # 3D 변환 노드
    │
    └── VPP.App/                           # WPF 애플리케이션
        ├── VPP.App.csproj
        ├── App.xaml                       # 애플리케이션 진입점
        ├── App.xaml.cs
        ├── ViewModels/
        │   └── MainViewModel.cs           # 메인 뷰모델 (MVVM)
        ├── Views/
        │   ├── MainWindow.xaml            # 메인 UI
        │   └── MainWindow.xaml.cs
        ├── Rendering/
        │   └── GpuPointCloudRenderer.cs   # GPU 렌더링 로직
        ├── Converters/
        │   └── BoolToVisibilityConverter.cs # XAML 값 변환기
        └── Resources/
            └── Styles.xaml                # UI 스타일 정의
```

### 어셈블리 의존성

```
VPP.App (WPF Application)
    ├─ depends on → VPP.Core
    ├─ depends on → VPP.Plugins.PointCloud
    ├─ depends on → HelixToolkit.Wpf.SharpDX
    └─ depends on → CommunityToolkit.Mvvm

VPP.Plugins.PointCloud (Class Library)
    └─ depends on → VPP.Core

VPP.Core (Class Library)
    ├─ depends on → System.Composition (MEF)
    └─ depends on → Newtonsoft.Json
```

---

## 🔌 노드 시스템

### 노드 라이프사이클

```
1. Plugin Discovery (App 시작 시)
   PluginService scans assemblies for [NodeInfo] attributes
        ↓
2. Node Registration
   Plugins register their node types with metadata
        ↓
3. Node Creation (사용자가 노드 추가 시)
   PluginService.CreateNode() via reflection
        ↓
4. Node Configuration
   User sets parameters in UI (PropertyGrid style)
        ↓
5. Graph Execution (사용자가 Execute 클릭 시)
   ExecutionEngine.ExecuteAsync()
        ↓
6. Topological Sort
   Determine execution order based on connections
        ↓
7. Node Execution (순차적)
   foreach node in sorted order:
       await node.ExecuteCoreAsync(context)
        ↓
8. Visualization Update
   After each node: Update 3D viewport
```

### 노드 카테고리 및 타입

#### Point Cloud/IO
**ImportPointCloudNode**
- **목적**: 파일에서 포인트 클라우드 로드
- **지원 포맷**: PLY (ASCII), PCD, XYZ, CSV, TXT
- **파라미터**:
  - `FilePath` (string): 파일 경로
- **컨텍스트 출력**: `PointCloud_{NodeId}` → PointCloudData
- **구현 특징**:
  - 비동기 스트리밍으로 대용량 파일 처리
  - Bounding Box 자동 계산
  - 색상 정보 파싱 (지원 시)

#### Point Cloud/ROI
**ROIDrawNode**
- **목적**: 3D 관심 영역 정의
- **ROI 타입**: Box, Cylinder, Sphere
- **파라미터**:
  - `ROIType` (enum): Box/Cylinder/Sphere
  - `CenterX/Y/Z` (double): ROI 중심 좌표
  - `SizeX/Y/Z` (double): Box의 크기
  - `Radius` (double): Cylinder/Sphere의 반경
  - `Height` (double): Cylinder의 높이
- **컨텍스트 출력**: `ROI_{NodeId}` → ROI3D
- **시각화**: 노란색 와이어프레임 + 빨간색 중심 마커

**ROIFilterNode**
- **목적**: ROI 내부의 포인트만 추출
- **파라미터**:
  - `Enabled` (bool): 필터링 활성화/비활성화
- **컨텍스트 입력**: `PointCloud_{SourceId}`, `ROI_{SourceId}`
- **컨텍스트 출력**: `FilteredCloud` → PointCloudData
- **알고리즘**:
  - Box: AABB (Axis-Aligned Bounding Box) 체크
  - Cylinder: 2D 거리 + 높이 체크
  - Sphere: 3D 유클리디안 거리 체크

#### Point Cloud/Detection
**CircleDetectionNode**
- **목적**: 포인트 클라우드에서 원형 형상 검출
- **알고리즘**: RANSAC (Random Sample Consensus)
- **파라미터**:
  - `MaxIterations` (int): RANSAC 최대 반복 횟수 (기본: 1000)
  - `DistanceThreshold` (double): 인라이어 거리 임계값 (기본: 0.01)
  - `MinRadius/MaxRadius` (double): 유효 반경 범위
  - `MinInlierRatio` (double): 최소 인라이어 비율 (기본: 0.3)
  - `AutoDetect` (bool): 자동 검출 모드
  - `PlaneAxis` (enum): XY/XZ/YZ 평면 선택
- **컨텍스트 입력**: `FilteredCloud` (또는 `PointCloud_{Id}`)
- **컨텍스트 출력**:
  - `CircleResult` → CircleDetectionResult
  - `DetectedCircleCloud` → PointCloudData (시각화용)
- **상세 알고리즘**: [아래 섹션 참조](#ransac-기반-원형-검출)

#### Point Cloud/Inspection
**InspectionNode**
- **목적**: 검출된 원형이 사양을 만족하는지 검증
- **파라미터** (사양):
  - `RadiusMin/RadiusMax` (double): 허용 반경 범위
  - `CenterXMin/Max`, `CenterYMin/Max` (double): 중심 위치 공차
  - `MaxFitError` (double): 최대 피팅 오차
  - `MinInliers` (int): 최소 인라이어 개수
- **컨텍스트 입력**: `CircleResult`
- **컨텍스트 출력**: `InspectionResult` → InspectionResult
- **검증 로직**:
  ```
  Pass if:
    RadiusMin ≤ Detected Radius ≤ RadiusMax AND
    CenterXMin ≤ Center.X ≤ CenterXMax AND
    CenterYMin ≤ Center.Y ≤ CenterYMax AND
    FitError ≤ MaxFitError AND
    Inliers ≥ MinInliers
  ```

#### Point Cloud/Transform
**RigidTransformNode**
- **목적**: 포인트 클라우드에 강체 변환 적용 (회전 + 평행이동)
- **파라미터**:
  - `TransformMatrix` (4x4 Matrix): 변환 행렬
- **컨텍스트 입력**: 모든 `PointCloud_{Id}` (자동 수집)
- **컨텍스트 출력**: 변환된 `PointCloud_{Id}` (덮어쓰기)
- **사용 사례**:
  - 여러 스캔 데이터 정합 (Registration)
  - 좌표계 변환

### 파라미터 시스템 (New)

#### 구조
```csharp
public interface IParameter
{
    string Name { get; }
    Type ValueType { get; }
    object Value { get; set; }
    string Description { get; }
}

public class Parameter<T> : IParameter
{
    public T Value { get; set; }
    // INotifyPropertyChanged for UI binding
}
```

#### 장점
- **타입 안전성**: 제네릭으로 타입 체크
- **UI 바인딩**: ObservableObject로 자동 업데이트
- **검증**: 값 변경 시 유효성 검사 가능
- **유연성**: 연결 없이도 노드 설정 가능

---

## ✨ 주요 기능

### 1. 노드 기반 그래프 에디터
- **드래그 앤 드롭**: 노드를 캔버스에 배치
- **연결 관리**: 노드 간 데이터 흐름 정의
- **순환 검출**: 무한 루프 방지
- **시각적 피드백**: 실행 중인 노드 하이라이트

### 2. 다중 포맷 포인트 클라우드 로드
- **PLY (Polygon File Format)**: 헤더 파싱, ASCII 포맷
- **PCD (Point Cloud Data)**: ROS/PCL 표준 포맷
- **XYZ**: 단순 좌표 텍스트 파일
- **CSV/TXT**: 쉼표 또는 공백 구분 값

### 3. 3D ROI (Region of Interest) 정의
- **Box**: 직육면체 영역 (AABB)
- **Cylinder**: 원기둥 영역
- **Sphere**: 구 영역
- **인터랙티브 편집**: 파라미터 조정 시 실시간 시각화

### 4. RANSAC 기반 원형 검출
- **자동 평면 선택**: 포인트 분포에 따라 XY/XZ/YZ 자동 선택
- **경계 포인트 최적화**: 그리드 기반 경계 검출로 정확도 향상
- **수동/자동 모드**: AutoDetect 또는 수동 트리거
- **파라미터 조정**: MaxIterations, DistanceThreshold 등

### 5. 합격/불합격 검사
- **사양 검증**: 반경, 중심 위치, 피팅 오차, 인라이어 수 체크
- **상세 리포트**: 각 측정값과 허용 범위 비교
- **Pass/Fail 표시**: UI에 검사 결과 시각화

### 6. GPU 가속 3D 시각화
- **실시간 렌더링**: 수백만 포인트를 60fps로 표시
- **LOD 시스템**: 자동 품질 조정으로 성능 유지
- **다중 레이어**: 포인트 클라우드 + ROI + 검출 결과 동시 표시
- **카메라 컨트롤**: 회전, 팬, 줌

### 7. 다중 클라우드 지원
- **여러 Import 노드**: 동시에 여러 포인트 클라우드 로드
- **Transform 체인**: 좌표계 정합
- **통합 시각화**: 하나의 뷰포트에 모든 클라우드 표시

### 8. 워크플로우 저장/로드
- **JSON 직렬화**: 그래프를 `.vpp.json` 파일로 저장
- **재현 가능성**: 저장된 워크플로우를 로드하여 동일한 작업 반복
- **버전 관리**: JSON 포맷으로 Git 등에서 diff 가능

### 9. 수동 검출 트리거
- **UI 버튼**: "Detect Circle" 버튼으로 즉시 검출 실행
- **실시간 피드백**: 검출 결과 즉시 시각화

### 10. 상태 메시지 및 진행 상황
- **실행 로그**: 각 노드의 실행 상태 표시
- **에러 처리**: 노드 실행 실패 시 상세 에러 메시지
- **진행률 표시**: 장시간 작업 시 진행 상황 피드백

---

## 🔬 알고리즘 상세

### RANSAC 기반 원형 검출

**RANSAC (Random Sample Consensus)**는 노이즈가 많은 데이터에서 모델을 추정하는 강건한 알고리즘입니다.

#### 알고리즘 단계

```
입력: 3D Point Cloud
출력: Circle (Center, Radius, Normal) in 3D

1. 평면 선택 및 투영
   ├─ ROI 타입 또는 포인트 분포 분석
   ├─ 최적 투영 평면 선택 (XY, XZ, YZ)
   └─ 3D 포인트 → 2D 포인트 투영

2. 경계 포인트 검출 (Boundary Detection)
   ├─ 2D 평면을 그리드로 분할
   ├─ 각 셀에서 가장자리 포인트 찾기
   └─ 경계 포인트 리스트 생성 (구멍 가장자리)

3. RANSAC 반복 (MaxIterations 동안)
   ├─ 랜덤하게 3개의 경계 포인트 샘플링
   ├─ 3점을 통과하는 원 계산 (기하학적 방법)
   │   ├─ 외심(circumcenter) 계산
   │   └─ 반경 계산
   ├─ 반경이 [MinRadius, MaxRadius] 범위 내인지 체크
   ├─ 인라이어 카운팅
   │   └─ 각 포인트와 원 경계의 거리 < DistanceThreshold
   ├─ 인라이어 비율 계산 (inliers / total points)
   └─ 최고 점수 업데이트 (인라이어 수가 많을수록 좋음)

4. 결과 검증
   ├─ 인라이어 비율 ≥ MinInlierRatio ?
   └─ Yes: 검출 성공 / No: 검출 실패

5. 3D 공간으로 역투영
   ├─ 2D 원 → 3D 원으로 변환
   ├─ Normal 벡터 설정 (투영 평면에 수직)
   └─ CircleDetectionResult 생성

출력: CircleDetectionResult
{
    Center3D: (x, y, z),
    Radius: r,
    Normal: (nx, ny, nz),
    Inliers: [points],
    FitError: average_distance
}
```

#### 3점으로 원 계산 (기하학)

```
주어진 3점: P1(x1,y1), P2(x2,y2), P3(x3,y3)

1. 두 현(chord)의 중점:
   M1 = (P1 + P2) / 2
   M2 = (P2 + P3) / 2

2. 각 현에 수직인 벡터:
   V1 = Perpendicular(P2 - P1)
   V2 = Perpendicular(P3 - P2)

3. 두 수직이등분선의 교점 = 원의 중심:
   Center = Intersection(M1 + t*V1, M2 + s*V2)

4. 반경:
   Radius = Distance(Center, P1)
```

#### 경계 포인트 검출

```
2D 포인트 클라우드 입력

1. Bounding Box 계산
2. 그리드 생성 (예: 50x50 셀)
3. 각 포인트를 해당 셀에 할당
4. 각 셀에 대해:
   ├─ 포인트가 있는가?
   └─ 인접 8개 셀 중 비어있는 셀이 있는가?
       └─ Yes: 경계 포인트
5. 경계 포인트 리스트 반환
```

이 방법은 구멍의 가장자리만 선택하여 RANSAC의 샘플 품질을 향상시킵니다.

### ROI 필터링 알고리즘

#### Box (직육면체)
```csharp
bool IsInsideBox(Point3D p, ROI3D box)
{
    return p.X >= box.Center.X - box.SizeX/2 &&
           p.X <= box.Center.X + box.SizeX/2 &&
           p.Y >= box.Center.Y - box.SizeY/2 &&
           p.Y <= box.Center.Y + box.SizeY/2 &&
           p.Z >= box.Center.Z - box.SizeZ/2 &&
           p.Z <= box.Center.Z + box.SizeZ/2;
}
```

#### Cylinder (원기둥)
```csharp
bool IsInsideCylinder(Point3D p, ROI3D cyl)
{
    // Z축 기준 원기둥
    double dx = p.X - cyl.Center.X;
    double dy = p.Y - cyl.Center.Y;
    double distXY = sqrt(dx*dx + dy*dy);

    return distXY <= cyl.Radius &&
           p.Z >= cyl.Center.Z - cyl.Height/2 &&
           p.Z <= cyl.Center.Z + cyl.Height/2;
}
```

#### Sphere (구)
```csharp
bool IsInsideSphere(Point3D p, ROI3D sph)
{
    double dx = p.X - sph.Center.X;
    double dy = p.Y - sph.Center.Y;
    double dz = p.Z - sph.Center.Z;
    double dist = sqrt(dx*dx + dy*dy + dz*dz);

    return dist <= sph.Radius;
}
```

---

## 🛠 기술 스택

### 프레임워크 및 언어
| 항목 | 버전 | 용도 |
|------|------|------|
| **.NET** | 8.0 | 런타임 프레임워크 |
| **C#** | 12 | 프로그래밍 언어 |
| **WPF** | .NET 8 | 데스크톱 UI 프레임워크 |

### 핵심 라이브러리
| 라이브러리 | 버전 | 용도 |
|-----------|------|------|
| **CommunityToolkit.Mvvm** | 8.2.2 | MVVM 패턴 구현 (RelayCommand, ObservableProperty) |
| **HelixToolkit.Wpf.SharpDX** | 2.25.0 | 3D 렌더링 (GPU 가속) |
| **SharpDX.Mathematics** | 4.2.0 | 3D 수학 라이브러리 (Vector3, Matrix4x4) |
| **Newtonsoft.Json** | 13.0.3 | JSON 직렬화/역직렬화 |
| **System.Composition** | 8.0.0 | MEF (플러그인 디스커버리) |

### 개발 도구
- **Visual Studio 2022** (권장) 또는 **Rider**
- **.NET 8 SDK**
- **Git** (버전 관리)

### 그래픽 API 스택
```
Application Code (C#)
    ↓
HelixToolkit.Wpf.SharpDX (Rendering Library)
    ↓
SharpDX (.NET Wrapper)
    ↓
DirectX 11 (Windows Graphics API)
    ↓
GPU Driver
    ↓
Graphics Hardware (GPU)
```

---

## 📖 사용 방법

### 빌드

```bash
# 프로젝트 클론
git clone <repository-url>
cd VisualProgrammingPlatform

# 솔루션 빌드
dotnet build VisualProgrammingPlatform.sln

# 또는 Release 모드
dotnet build -c Release
```

### 실행

```bash
# Debug 모드 실행
dotnet run --project src/VPP.App/VPP.App.csproj

# 또는 빌드된 실행 파일
./src/VPP.App/bin/Debug/net8.0-windows/VPP.App.exe
```

### 기본 워크플로우

#### 1단계: 워크플로우 생성
1. 애플리케이션 실행
2. 상단 메뉴에서 **"Create Workflow"** 클릭
3. 기본 노드들이 자동으로 배치됨:
   - Import Point Cloud
   - ROI Draw
   - ROI Filter
   - Circle Detection
   - Inspection

#### 2단계: 포인트 클라우드 로드
1. **"Load Point Cloud"** 버튼 클릭
2. PLY, PCD, XYZ, CSV 파일 선택
3. 3D 뷰포트에 포인트 클라우드 표시

#### 3단계: ROI 설정
1. **ROI Draw 노드** 선택
2. 프로퍼티 패널에서 파라미터 조정:
   - ROI Type: Box/Cylinder/Sphere
   - Center: 중심 좌표
   - Size/Radius/Height: 크기
3. 뷰포트에서 노란색 와이어프레임으로 ROI 확인

#### 4단계: 원형 검출
1. **Circle Detection 노드** 파라미터 조정:
   - MaxIterations: 1000 (기본값)
   - DistanceThreshold: 0.01
   - MinRadius/MaxRadius: 예상 반경 범위
2. **"Execute"** 버튼 클릭 또는
3. **"Detect Circle"** 버튼으로 수동 실행

#### 5단계: 결과 확인
- **3D 뷰포트**:
  - 노란색 포인트: 검출된 원 경계
  - 빨간색 원: 피팅된 원 외곽선
- **상태 메시지**:
  - "Circle detected: Center=(...), Radius=..."
- **Inspection 노드 결과**:
  - "Pass" 또는 "Fail" 메시지

#### 6단계: 워크플로우 저장
1. **"Save Workflow"** 클릭
2. `.vpp.json` 파일로 저장
3. 나중에 **"Load Workflow"**로 재사용

---

## 🔧 확장 개발

### 새로운 노드 추가하기

#### 1단계: 노드 클래스 작성

```csharp
using VPP.Core.Attributes;
using VPP.Core.Models;

namespace MyPlugin.Nodes
{
    [NodeInfo("My Custom Node", "MyCategory", "노드 설명")]
    public class MyCustomNode : NodeBase
    {
        public MyCustomNode()
        {
            // 파라미터 정의
            AddParameter("InputValue", 0.0, "입력 값");
            AddParameter("Multiplier", 2.0, "곱셈 계수");
        }

        protected override Task ExecuteCoreAsync(
            ExecutionContext context,
            CancellationToken cancellationToken)
        {
            // 파라미터 읽기
            double input = GetParameter<double>("InputValue");
            double multiplier = GetParameter<double>("Multiplier");

            // 계산
            double result = input * multiplier;

            // 컨텍스트에 저장
            context[$"Result_{Id}"] = result;

            // 로그 출력
            LogMessage($"결과: {result}");

            return Task.CompletedTask;
        }
    }
}
```

#### 2단계: 플러그인 등록

```csharp
using VPP.Core.Interfaces;
using System.Collections.Generic;

namespace MyPlugin
{
    public class MyPlugin : IPlugin
    {
        public string Name => "My Custom Plugin";
        public string Version => "1.0.0";

        public IEnumerable<Type> NodeTypes => new[]
        {
            typeof(MyCustomNode),
            // 다른 노드들...
        };
    }
}
```

#### 3단계: 빌드 및 배포

```bash
# 플러그인 프로젝트 빌드
dotnet build MyPlugin/MyPlugin.csproj

# DLL을 애플리케이션 폴더에 복사
cp MyPlugin/bin/Debug/net8.0/MyPlugin.dll src/VPP.App/bin/Debug/net8.0-windows/
```

### 고급 노드 패턴

#### 컨텍스트 데이터 읽기

```csharp
protected override Task ExecuteCoreAsync(
    ExecutionContext context,
    CancellationToken cancellationToken)
{
    // 특정 키로 데이터 읽기
    if (context.TryGetValue("PointCloud_node1", out var obj))
    {
        var cloud = obj as PointCloudData;
        // 처리...
    }

    // 모든 포인트 클라우드 읽기
    var allClouds = context.Keys
        .Where(k => k.StartsWith("PointCloud_"))
        .Select(k => context[k] as PointCloudData)
        .Where(c => c != null)
        .ToList();

    return Task.CompletedTask;
}
```

#### 비동기 파일 I/O

```csharp
protected override async Task ExecuteCoreAsync(
    ExecutionContext context,
    CancellationToken cancellationToken)
{
    string filePath = GetParameter<string>("FilePath");

    using (var reader = new StreamReader(filePath))
    {
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 파싱 로직...
        }
    }
}
```

#### 진행 상황 보고

```csharp
protected override async Task ExecuteCoreAsync(
    ExecutionContext context,
    CancellationToken cancellationToken)
{
    int totalSteps = 100;
    for (int i = 0; i < totalSteps; i++)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 작업 수행...
        await Task.Delay(10);

        // 진행 상황 보고
        ReportProgress(i, totalSteps, $"처리 중: {i}/{totalSteps}");
    }
}
```

---

## 📚 참고 문서

### 내부 문서
- **CIRCLE_DETECTION_FEATURE.md**: 원형 검출 기능 상세 문서
- **코드 주석**: 각 클래스 및 메서드에 XML 주석 포함

### 외부 리소스
- [HelixToolkit Documentation](https://github.com/helix-toolkit/helix-toolkit)
- [CommunityToolkit.Mvvm Docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [RANSAC Algorithm](https://en.wikipedia.org/wiki/Random_sample_consensus)
- [WPF Documentation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)

---

## 🐛 알려진 이슈 및 제한사항

1. **포트 시스템 Deprecated**: 명시적 포트 연결 대신 컨텍스트 기반 데이터 공유 사용 권장
2. **GPU 메모리 제한**: 1천만 개 이상의 포인트는 LOD로 간소화됨
3. **Windows 전용**: WPF 및 DirectX로 인해 Windows에서만 동작
4. **단일 스레드 실행**: 노드 실행이 순차적 (병렬 실행 미구현)
5. **Undo/Redo 미구현**: 커맨드 패턴 준비되어 있으나 미완성

---

## 🚀 향후 계획

- [ ] **병렬 노드 실행**: 독립적인 노드를 병렬로 실행하여 성능 향상
- [ ] **Undo/Redo 구현**: 커맨드 히스토리 스택 완성
- [ ] **더 많은 형상 검출**: 직선, 평면, 구 검출 노드 추가
- [ ] **Python 스크립트 노드**: Python 코드로 커스텀 처리
- [ ] **클라우드 정합 노드**: ICP 알고리즘 구현
- [ ] **성능 최적화**: Octree, KD-Tree 등 공간 분할 구조 도입
- [ ] **Linux/macOS 지원**: Avalonia UI로 크로스 플랫폼 포팅

---

## 📄 라이선스

MIT License

Copyright (c) 2024

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## 👥 기여

이슈 및 풀 리퀘스트를 환영합니다!

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📧 문의

프로젝트 관련 문의는 이슈 트래커를 이용해주세요.

---

**Built with ❤️ using .NET 8 and WPF**
