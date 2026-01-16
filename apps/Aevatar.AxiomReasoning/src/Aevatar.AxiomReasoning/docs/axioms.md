# Axiom Set (O1–O4)

> 目的：把系统的“最小公理”整理成**可引用、可推理、可校验**的格式，方便 `axiom_reasoning` 工作流解析与逐步共识推理。

---

## 0. 符号表（Symbols / Glossary）

- \(\mathcal{H}\)：全局希尔伯特空间（复、可分）。
- \(|\Psi\rangle \in \mathcal{H}\)：宇宙的全局量子态向量（归一化）。
- \(\langle \Psi|\Psi\rangle = 1\)：归一化条件。
- \(t\)：外部时间参数（O1 认为“本体论上不存在/不作为基本参数”）。
- \(A\)：某因果封闭有界区域的边界“面积”（或与面积同阶的边界尺度量）。
- \(l_P\)：普朗克长度（尺度参数）。
- \(\mathcal{H}_{region}\)：某有界因果封闭区域对应的“有效”希尔伯特空间。
- \(G=(V,E)\)：可数图；顶点 \(V\) 对应局部自由度，边 \(E\) 对应局域相互作用关系。
- \(\mathcal{H}_v\)：顶点 \(v\) 的局部希尔伯特空间。
- \(U\)：整体更新/演化算符（可由局部门序列构造）。
- \(U^{(k)}_{local}\)：第 \(k\) 个局部门，只作用于相邻顶点（或有限邻域）。
- \(\partial G\)：图的“边界”（boundary）。
- \(\mathcal{H}_{bulk}\)、\(\mathcal{H}_{\partial G}\)：体与边界的希尔伯特空间。
- \(\Phi\)：全息等距映射（isometry）。

---

## O1. 静态宇宙向量（The Static Universe Vector）

### 公理表述（Axiom）
物理宇宙在本体论上同构于 \(\mathcal{H}\) 中一个**单一、唯一且归一化**的向量 \(|\Psi\rangle\)，并且不存在作为基本结构的外部时间演化。

### 数学表述（Formal）
\[
\partial_t |\Psi\rangle = 0, \quad \langle \Psi|\Psi\rangle = 1
\]

### 语义解释（Semantics）
- **全局无时间**：不存在外部时间参数 \(t\) 驱动 \(|\Psi\rangle\) 的本体论演化；“时间”若出现，应是子系统/有效描述的涌现量。 [1]

---

## O2. 有限信息（Finite Information）

### 公理表述（Axiom）
对任何**因果封闭且有界**的区域，其有效希尔伯特空间维度严格有限，并受边界面积尺度控制。

### 数学表述（Formal）
\[
\dim(\mathcal{H}_{region}) < \infty, \quad
\dim(\mathcal{H}_{region}) \sim \exp\left(\frac{A}{4l_P^2}\right)
\]

### 语义解释（Semantics）
- **信息密度有限**：自然不支持无限信息密度（Bekenstein bound 直觉）。
- **连续变量是近似**：场/坐标等连续自由度应被视为涌现的有效描述。 [1]

---

## O3. 因果局域性（Causal Locality）

### 公理表述（Axiom）
\(\mathcal{H}\) 在可数图 \(G=(V,E)\) 上分解；整体更新算符 \(U\) 可分解为仅作用于相邻顶点（或有限邻域）的局部门序列。

### 数学表述（Formal）
\[
\mathcal{H}=\bigotimes_{v\in V}\mathcal{H}_v, \quad
U=\prod_k U^{(k)}_{local}
\]

### 语义解释（Semantics）
- **传播受限**：局域门结构对信息传播施加“光锥式”的因果约束；连续极限下可恢复 SR-like 因果结构。 [1]

---

## O4. 全息映射（The Holographic Mapping）

### 公理表述（Axiom）
存在一个**全息等距映射** \(\Phi\)，将体（bulk）自由度映射到边界 \(\partial G\) 的自由度。

### 数学表述（Formal）
\[
\Phi:\mathcal{H}_{bulk}\to \mathcal{H}_{\partial G},
\quad \text{and }\Phi\text{ is an isometry}
\]

### 语义解释（Semantics）
- **张量网络实现**：可由“Golden MERA”等张量网络实现，具有量子纠错码（QECC）结构，保护体几何结构免受边界擦除影响。 [1]

---

## 1. 可直接用于推理的“安全推论”（Derived Facts You Can Use Safely）

> 下面推论是“几乎纯数学层面”从公理措辞直接读出的，适合当作推理的中间台阶（尤其在 `vote` 共识场景中）。

1) 由 O2：对任意有界因果封闭区域，\(\mathcal{H}_{region}\) 的维度有限，因此可区分正交态数量有限。  
2) 由 O3：若每一步更新只作用于有限邻域，则有限步更新只能影响有限图距离范围内的自由度（直觉上产生“因果锥”结构）。  
3) 由 O4（等距）：若 \(\Phi\) 为等距映射，则保持内积与范数；特别地，\(\Phi(x)=0\Rightarrow x=0\)，因此 \(\Phi\) 单射，\(\dim(\mathcal{H}_{bulk})\le \dim(\mathcal{H}_{\partial G})\)（在有限维情形下）。  

---

## 2. 适用范围与非目标（Scope / Non-goals）

- **适用**：讨论“时间/几何/因果/全息/纠错”等从量子信息结构涌现的推理。  
- **不保证可证**：经典欧氏几何命题（如勾股定理）、具体物理常数数值结论等，除非额外加入对应几何/动力学公理。  

---

## 参考

[1] 来源文献/笔记（此处保留占位，后续可替换为真实引用条目）。