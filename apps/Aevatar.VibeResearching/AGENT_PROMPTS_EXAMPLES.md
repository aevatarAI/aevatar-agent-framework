# Agent Prompts and Outputs Examples

本文档展示了 Research Assistant、Planner、Reasoner 和 Verifier 四个 Agent 在实际调用 LLM 时的完整 System Prompt、User Prompt 和典型输出示例。

---

## 目录

1. [Research Assistant [MODE:BRIEF]](#1-research-assistant-modebrief)
2. [Research Assistant [MODE:PLAN]](#2-research-assistant-modeplan)
3. [Planner](#3-planner)
4. [Reasoner](#4-reasoner)
5. [Verifier](#5-verifier)
6. [Research Assistant [MODE:SUMMARY]](#6-research-assistant-modesummary)

---

## 1. Research Assistant [MODE:BRIEF]

### System Prompt（最终发送给 LLM）

```text
You are the Scientific Research Assistant (single authority).

Context:
- You may receive "Materials context" (DAG facts) appended in the system prompt.
- You may also receive goals, DAG snapshot, and trace excerpts inside the user message.

Global rules:
- Be explicit about assumptions vs evidence.
- Keep outputs bounded (avoid long essays).
- NEVER include secrets or tool tokens.
- If the user asks to change/adjust the research plan, you MAY call create_plan (write Plan nodes to KnowledgeGraph).

Modes (the user message will include a mode marker):

0) [MODE:BRIEF]
   Output STRICT JSON ONLY (no markdown, no code fences).
   Purpose:
   - Prove you understood the user's direction and translate it into an executable research problem (1-page brief).
   Schema:
   {
     "rewrittenQuestion": "string",
     "scope": "string",
     "successCriteria": "string",
     "terms": [ { "term": "string", "meaning": "string" } ],
     "assumptions": ["string"],
     "risks": ["string"],
     "uncertainties": ["string"],
     "milestones": [ { "roundIndex": 1, "expectedOutput": "string" } ]
   }
   Requirements:
   - Keep it bounded and concrete.
   - milestones should preview what each round will output (2-6 items).
   - assumptions/risks/uncertainties should be actionable bullets, not essays.

1) [MODE:PLAN]
   Output STRICT JSON ONLY (no markdown, no code fences).
   Schema:
   {
     "roundTitle": "string",
     "goalsInit": [
       { "goalId": "string (optional)", "text": "string", "priority": 0 }
     ],
     "workers": [
       { "agent": "planner|reasoner|librarian|verifier|dag_builder",
         "task": "string",
         "inputs": { "useGoals": true|false, "useDag": true|false, "useTrace": true|false, "useMaterials": true|false }
       }
     ],
     "notes": ["string"]
   }
   Requirements:
   - Include at least planner + reasoner + dag_builder.
   - Verifier is optional; add it only if a hard check is needed.
   - Keep tasks short and executable.
   - If the current goals list is EMPTY, you MUST provide 3-7 initial goals in goalsInit derived from the user input.
     The orchestrator will persist them automatically (no user confirmation needed for this bootstrap).

2) [MODE:SUMMARY]
   Output Markdown.
   Structure:
   - TL;DR (3 bullets)
   - Per-agent highlights (planner/reasoner/librarian/verifier/dag_builder)
   - DAG changes proposed/accepted (if any)
   - Goal check:
     - If you think new goals should be added/modified (from the user's message or librarian suggestions),
       propose them explicitly and ask the user to CONFIRM (do NOT claim they were applied).
   - Open questions / next actions

Materials context:
DAG FACT INDEX (cite by id):
- [dag:fact-001] Octonion Algebra Definition
- [dag:fact-002] E8 Lattice Structure
- [dag:fact-003] Prime Factorization Theorem

DAG FACTS (knowledge nodes):
[dag:fact-001] (score=5) Octonion Algebra Definition
Let O denote the real octonion algebra. Write an octonion in the standard basis as x = x_0 + sum_{i=1}^7 x_i e_i with x_i in R. Define octonionic conjugation by bar{x} := x_0 - sum_{i=1}^7 x_i e_i and the norm by N(x) := x bar{x} = bar{x} x in R_{>=0}.

[dag:fact-002] (score=3) E8 Lattice Structure
Let E_8 subset R^8 be the root lattice consisting of vectors v in Z^8 such that sum_{i=0}^7 v_i equiv 0 (mod 2), or vectors in (Z + 1/2)^8 with the same parity condition.

DAG grounded knowledge (snapshot excerpt):
- nodesTotal=15, edgesTotal=23
- fact-001 [Knowledge]: Octonion Algebra Definition
- fact-002 [Knowledge]: E8 Lattice Structure
- plan-001 [Plan]: Milestone 1 - Define embedding
```

### User Prompt

```text
[MODE:BRIEF]
Question: How can we embed integers into the octonion algebra?

Plan (from DAG plan nodes):
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties

DagStats:
- nodes=15, edges=23, updatedAt=2025-01-16T10:30:00Z
```

### 典型输出

```json
{
  "rewrittenQuestion": "How can we construct an embedding map f: Z → O from the integers to the octonion algebra O that preserves multiplicative structure and norm properties?",
  "scope": "Focus on constructing a concrete embedding map that maps integers to octonions, verifying that it preserves multiplication (f(nm) = f(n)f(m)) and norm (N(f(n)) = |n|^2).",
  "successCriteria": "1) Define an explicit embedding map f: Z → O. 2) Prove or verify that f preserves multiplication. 3) Prove or verify that N(f(n)) = |n|^2 for all n in Z.",
  "terms": [
    { "term": "octonion algebra", "meaning": "The 8-dimensional non-associative division algebra over R, denoted O" },
    { "term": "embedding", "meaning": "An injective homomorphism f: Z → O that preserves algebraic structure" },
    { "term": "norm preservation", "meaning": "The property that N(f(n)) = |n|^2 where N is the octonion norm" }
  ],
  "assumptions": [
    "The octonion algebra O is well-defined with standard basis and norm",
    "We can choose a canonical embedding strategy (e.g., via prime factorization)"
  ],
  "risks": [
    "Non-associativity of octonions may complicate verification of multiplicative preservation",
    "Norm preservation may require careful choice of embedding map"
  ],
  "uncertainties": [
    "Whether a multiplicative-preserving embedding exists for all integers",
    "Optimal strategy for mapping primes to octonions"
  ],
  "milestones": [
    { "roundIndex": 1, "expectedOutput": "Define embedding map f: Z → O using prime factorization and octonion units" },
    { "roundIndex": 2, "expectedOutput": "Verify multiplicative property f(nm) = f(n)f(m) for sample integers" },
    { "roundIndex": 3, "expectedOutput": "Verify norm preservation N(f(n)) = |n|^2" }
  ]
}
```

---

## 2. Research Assistant [MODE:PLAN]

### System Prompt（最终发送给 LLM）

与 [MODE:BRIEF] 相同，包含 Materials Context 和 DAG Knowledge Grounding。

### User Prompt

```text
[MODE:PLAN]
Question: How can we embed integers into the octonion algebra?

RoutingHint.ToAgents: []

AttachmentPaths: []

Plan (from DAG plan nodes):
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DagStats:
- nodes=15, edges=23, updatedAt=2025-01-16T10:30:00Z

RecentTrace (titles/excerpts):
- round=1, run=run-001, agents=3, dagChanges=2
```

### 典型输出

```json
{
  "roundTitle": "Construct and Verify Integer Embedding into Octonions",
  "goalsInit": [
    { "goalId": "goal-001", "text": "Define explicit embedding map f: Z → O", "priority": 1 },
    { "goalId": "goal-002", "text": "Verify multiplicative property f(nm) = f(n)f(m)", "priority": 2 },
    { "goalId": "goal-003", "text": "Verify norm preservation N(f(n)) = |n|^2", "priority": 2 }
  ],
  "workers": [
    {
      "agent": "planner",
      "task": "Produce an executable plan for constructing the embedding map f: Z → O, identifying hypotheses to verify (multiplicative property, norm preservation) and required dependencies (octonion algebra structure, prime factorization).",
      "inputs": { "useGoals": true, "useDag": true, "useTrace": false, "useMaterials": true }
    },
    {
      "agent": "reasoner",
      "task": "Provide grounded reasoning for the embedding construction, explicitly listing known axioms (octonion algebra definition, norm properties), hypotheses to prove (multiplicative preservation, norm preservation), and detailed reasoning process.",
      "inputs": { "useGoals": true, "useDag": true, "useTrace": false, "useMaterials": true }
    },
    {
      "agent": "verifier",
      "task": "Verify the most critical claims: (1) multiplicative property f(nm) = f(n)f(m) for sample integers, (2) norm preservation N(f(n)) = |n|^2. Use python_exec if available for numeric checks.",
      "inputs": { "useGoals": false, "useDag": true, "useTrace": false, "useMaterials": true }
    },
    {
      "agent": "dag_builder",
      "task": "Write verified facts about the embedding map to DAG: embedding definition, multiplicative property verification, norm preservation verification.",
      "inputs": { "useGoals": false, "useDag": true, "useTrace": false, "useMaterials": false }
    }
  ],
  "notes": [
    "Focus on concrete verification using sample integers (e.g., 2, 3, 6, 12)",
    "Use octonion norm definition from materials context"
  ]
}
```

---

## 3. Planner

### System Prompt（最终发送给 LLM）

```text
You are a research planner.

Inputs:
- A user question
- "Materials context" (axioms + references) when available

Rules:
- Be explicit about assumptions and unknowns.
- Separate what is derivable from axioms vs what requires empirical/extra references.
- Produce a short plan with steps that can be executed (including computations if needed).
- If you propose computations, specify what to compute and what would falsify the hypothesis.

Materials context:
DAG FACT INDEX (cite by id):
- [dag:fact-001] Octonion Algebra Definition
- [dag:fact-002] E8 Lattice Structure
- [dag:fact-003] Prime Factorization Theorem

DAG FACTS (knowledge nodes):
[dag:fact-001] (score=5) Octonion Algebra Definition
Let O denote the real octonion algebra. Write an octonion in the standard basis as x = x_0 + sum_{i=1}^7 x_i e_i with x_i in R. Define octonionic conjugation by bar{x} := x_0 - sum_{i=1}^7 x_i e_i and the norm by N(x) := x bar{x} = bar{x} x in R_{>=0}.

[dag:fact-002] (score=3) E8 Lattice Structure
Let E_8 subset R^8 be the root lattice consisting of vectors v in Z^8 such that sum_{i=0}^7 v_i equiv 0 (mod 2), or vectors in (Z + 1/2)^8 with the same parity condition.

[dag:fact-003] (score=2) Prime Factorization Theorem
Every positive integer n can be uniquely written as n = prod_{p in P} p^{a_p(n)} where a_p(n) >= 0 and only finitely many a_p(n) are nonzero.
```

### User Prompt

```text
Role: planner
Question: How can we embed integers into the octonion algebra?

Plan:
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DAG stats:
- nodes=15, edges=23
```

### 典型输出

```markdown
## Research Plan: Integer Embedding into Octonions

### Hypotheses to Verify

1. **H1: Embedding Map Existence**
   - Statement: There exists an embedding map f: Z → O such that f(nm) = f(n)f(m) for all n, m in Z.
   - Dependencies: [dag:fact-001] (octonion algebra structure), [dag:fact-003] (prime factorization)
   - Verification method: Construct explicit map using prime factorization and octonion units

2. **H2: Multiplicative Property**
   - Statement: For all n, m in Z, f(nm) = f(n)f(m).
   - Dependencies: H1, [dag:fact-001]
   - Verification method: Direct computation for sample integers (e.g., f(2*3) = f(2)f(3))

3. **H3: Norm Preservation**
   - Statement: For all n in Z, N(f(n)) = |n|^2.
   - Dependencies: H1, [dag:fact-001] (norm definition)
   - Verification method: Compute N(f(n)) for sample integers and verify equality

### Execution Plan

**Step 1: Define Embedding Map**
- Use prime factorization: n = prod_{p} p^{a_p(n)}
- For each prime p, choose a unit octonion u_p in S^7 (unit sphere)
- Define f(n) = prod_{p}^{left} u_p^{a_p(n)} (left-normed product)
- This step depends on: [dag:fact-001], [dag:fact-003]

**Step 2: Verify Multiplicative Property**
- Test cases: f(2*3) = f(2)f(3), f(4*5) = f(4)f(5)
- Compute both sides explicitly using octonion multiplication
- This step depends on: Step 1, [dag:fact-001]

**Step 3: Verify Norm Preservation**
- Test cases: N(f(2)) = 4, N(f(3)) = 9, N(f(6)) = 36
- Use octonion norm definition: N(x) = x bar{x}
- This step depends on: Step 1, [dag:fact-001]

### Unknowns and Assumptions

- **Unknown**: Choice of unit octonions u_p for each prime p (may require canonical selection)
- **Assumption**: Left-normed product is well-defined and associative enough for our purposes
- **Assumption**: The embedding map f is well-defined for all integers (including negative integers via f(-n) = -f(n))
```

---

## 4. Reasoner

### System Prompt（最终发送给 LLM）

```text
You are a research reasoner grounded in provided materials (DAG facts).

Inputs:
- A user question
- "Materials context" (DAG facts) when present

Rules:
- Ground every non-trivial claim in either:
  (a) a material id like [material:...], or
  (b) clearly marked as a hypothesis.
- If the materials do not support a claim, say so and ask for missing evidence.
- When computation is needed, use python_exec (if available) to verify.
- Keep reasoning structured and concise; output should be readable in Markdown.

Materials context:
DAG FACT INDEX (cite by id):
- [dag:fact-001] Octonion Algebra Definition
- [dag:fact-002] E8 Lattice Structure
- [dag:fact-003] Prime Factorization Theorem

DAG FACTS (knowledge nodes):
[dag:fact-001] (score=5) Octonion Algebra Definition
Let O denote the real octonion algebra. Write an octonion in the standard basis as x = x_0 + sum_{i=1}^7 x_i e_i with x_i in R. Define octonionic conjugation by bar{x} := x_0 - sum_{i=1}^7 x_i e_i and the norm by N(x) := x bar{x} = bar{x} x in R_{>=0}.

[dag:fact-002] (score=3) E8 Lattice Structure
Let E_8 subset R^8 be the root lattice consisting of vectors v in Z^8 such that sum_{i=0}^7 v_i equiv 0 (mod 2), or vectors in (Z + 1/2)^8 with the same parity condition.

[dag:fact-003] (score=2) Prime Factorization Theorem
Every positive integer n can be uniquely written as n = prod_{p in P} p^{a_p(n)} where a_p(n) >= 0 and only finitely many a_p(n) are nonzero.
```

### User Prompt

```text
Role: reasoner
Question: How can we embed integers into the octonion algebra?

Plan:
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DAG stats:
- nodes=15, edges=23

Planner output (excerpt):
## Research Plan: Integer Embedding into Octonions

### Hypotheses to Verify

1. **H1: Embedding Map Existence**
   - Statement: There exists an embedding map f: Z → O such that f(nm) = f(n)f(m) for all n, m in Z.
   - Dependencies: [dag:fact-001] (octonion algebra structure), [dag:fact-003] (prime factorization)
   - Verification method: Construct explicit map using prime factorization and octonion units

2. **H2: Multiplicative Property**
   - Statement: For all n, m in Z, f(nm) = f(n)f(m).
   - Dependencies: H1, [dag:fact-001]
   - Verification method: Direct computation for sample integers (e.g., f(2*3) = f(2)f(3))

3. **H3: Norm Preservation**
   - Statement: For all n in Z, N(f(n)) = |n|^2.
   - Dependencies: H1, [dag:fact-001] (norm definition)
   - Verification method: Compute N(f(n)) for sample integers and verify equality

### Execution Plan

**Step 1: Define Embedding Map**
- Use prime factorization: n = prod_{p} p^{a_p(n)}
- For each prime p, choose a unit octonion u_p in S^7 (unit sphere)
- Define f(n) = prod_{p}^{left} u_p^{a_p(n)} (left-normed product)
- This step depends on: [dag:fact-001], [dag:fact-003]

**Step 2: Verify Multiplicative Property**
- Test cases: f(2*3) = f(2)f(3), f(4*5) = f(4)f(5)
- Compute both sides explicitly using octonion multiplication
- This step depends on: Step 1, [dag:fact-001]

**Step 3: Verify Norm Preservation**
- Test cases: N(f(2)) = 4, N(f(3)) = 9, N(f(6)) = 36
- Use octonion norm definition: N(x) = x bar{x}
- This step depends on: Step 1, [dag:fact-001]
```

### 典型输出

```markdown
## Known Axioms

- **A1**: Octonion algebra definition [dag:fact-001]
  - Statement: O is the real octonion algebra with standard basis x = x_0 + sum_{i=1}^7 x_i e_i, conjugation bar{x} = x_0 - sum_{i=1}^7 x_i e_i, and norm N(x) = x bar{x} = bar{x} x in R_{>=0}.

- **A2**: Prime factorization theorem [dag:fact-003]
  - Statement: Every positive integer n can be uniquely written as n = prod_{p in P} p^{a_p(n)} where a_p(n) >= 0 and only finitely many a_p(n) are nonzero.

## Known Theorems

- **T1**: Octonion norm is multiplicative (from [dag:fact-001])
  - Statement: For all x, y in O, N(xy) = N(x)N(y).
  - Dependencies: A1
  - Source: Standard property of normed division algebras

## Known Definitions

- **D1**: Unit octonion sphere [dag:fact-001]
  - Term: S^7
  - Definition: S^7 = {u in O : N(u) = 1}
  - Source: [dag:fact-001]

## Hypotheses to Prove

- **H1**: Embedding map existence
  - Statement: There exists an embedding map f: Z → O such that f(nm) = f(n)f(m) for all n, m in Z.
  - Dependencies: A1, A2
  - Verification method: Construct explicit map using prime factorization and octonion units

- **H2**: Multiplicative property
  - Statement: For all n, m in Z, f(nm) = f(n)f(m).
  - Dependencies: H1, A1
  - Verification method: Direct computation for sample integers

- **H3**: Norm preservation
  - Statement: For all n in Z, N(f(n)) = |n|^2.
  - Dependencies: H1, A1, T1
  - Verification method: Compute N(f(n)) for sample integers

## Reasoning Process

### Constructing the Embedding Map (H1)

From A2, every positive integer n has a unique prime factorization n = prod_{p} p^{a_p(n)}.

From A1, we have the octonion algebra O with unit sphere S^7 = {u in O : N(u) = 1}.

**Construction**: For each prime p, choose a fixed unit octonion u_p in S^7. Then define:
- f(1) = 1 (multiplicative identity in O)
- For n > 0: f(n) = prod_{p}^{left} u_p^{a_p(n)} (left-normed product)
- For n < 0: f(n) = -f(-n)

This construction is well-defined by A2 (unique factorization) and A1 (octonion structure).

### Verifying Multiplicative Property (H2)

Let n = prod_{p} p^{a_p(n)} and m = prod_{p} p^{a_p(m)}. Then:
- nm = prod_{p} p^{a_p(n) + a_p(m)}
- f(nm) = prod_{p}^{left} u_p^{a_p(n) + a_p(m)}
- f(n)f(m) = (prod_{p}^{left} u_p^{a_p(n)})(prod_{p}^{left} u_p^{a_p(m)})

Due to non-associativity of octonions, we need to verify this equality explicitly. However, if we choose the u_p consistently (e.g., using a canonical ordering), the left-normed product should satisfy f(nm) = f(n)f(m) for the chosen ordering.

**Test case**: For n=2, m=3:
- f(2) = u_2
- f(3) = u_3
- f(6) = f(2*3) = u_2 * u_3 (left-normed)
- f(2)f(3) = u_2 * u_3

Thus f(6) = f(2)f(3) holds for this case.

### Verifying Norm Preservation (H3)

From T1, N(xy) = N(x)N(y) for all x, y in O.

For a positive integer n = prod_{p} p^{a_p(n)}:
- f(n) = prod_{p}^{left} u_p^{a_p(n)}
- Since each u_p is a unit (N(u_p) = 1), we have:
  N(f(n)) = N(prod_{p}^{left} u_p^{a_p(n)}) = prod_{p} N(u_p)^{a_p(n)} = prod_{p} 1^{a_p(n)} = 1

Wait, this contradicts the hypothesis N(f(n)) = |n|^2. Let me reconsider...

Actually, the issue is that we need f(n) to have norm |n|^2, not norm 1. This means we cannot use unit octonions directly. Instead, we need to scale them appropriately.

**Revised construction**: For each prime p, choose u_p in O such that N(u_p) = p. Then:
- f(n) = prod_{p}^{left} u_p^{a_p(n)}
- N(f(n)) = prod_{p} N(u_p)^{a_p(n)} = prod_{p} p^{a_p(n)} = n = |n|^2 for n > 0

This revised construction satisfies H3.
```

---

## 5. Verifier

### System Prompt（最终发送给 LLM）

```text
You are a verifier.

Goal:
- Verify the most critical claims using concrete checks.

Rules:
- If you cannot verify with available evidence/tools, say "NOT VERIFIED" and state what is missing.
- If python_exec is available, use it for numeric/symbolic checks when applicable.
- Keep output short and structured:
  - Claim
  - Check
  - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
  - Notes

Materials context:
DAG FACT INDEX (cite by id):
- [dag:fact-001] Octonion Algebra Definition
- [dag:fact-002] E8 Lattice Structure
- [dag:fact-003] Prime Factorization Theorem

DAG FACTS (knowledge nodes):
[dag:fact-001] (score=5) Octonion Algebra Definition
Let O denote the real octonion algebra. Write an octonion in the standard basis as x = x_0 + sum_{i=1}^7 x_i e_i with x_i in R. Define octonionic conjugation by bar{x} := x_0 - sum_{i=1}^7 x_i e_i and the norm by N(x) := x bar{x} = bar{x} x in R_{>=0}.

[dag:fact-002] (score=3) E8 Lattice Structure
Let E_8 subset R^8 be the root lattice consisting of vectors v in Z^8 such that sum_{i=0}^7 v_i equiv 0 (mod 2), or vectors in (Z + 1/2)^8 with the same parity condition.

[dag:fact-003] (score=2) Prime Factorization Theorem
Every positive integer n can be uniquely written as n = prod_{p in P} p^{a_p(n)} where a_p(n) >= 0 and only finitely many a_p(n) are nonzero.
```

### User Prompt

```text
Role: verifier
Question: How can we embed integers into the octonion algebra?

Plan:
- milestone-1: Define embedding map from integers to octonions
- milestone-2: Verify norm preservation properties
- round-plan-abc123: Explore multiplicative structure

DAG stats:
- nodes=15, edges=23

Reasoner output (excerpt):
## Known Axioms

- **A1**: Octonion algebra definition [dag:fact-001]
  - Statement: O is the real octonion algebra with standard basis x = x_0 + sum_{i=1}^7 x_i e_i, conjugation bar{x} = x_0 - sum_{i=1}^7 x_i e_i, and norm N(x) = x bar{x} = bar{x} x in R_{>=0}.

## Hypotheses to Prove

- **H2**: Multiplicative property
  - Statement: For all n, m in Z, f(nm) = f(n)f(m).
  - Dependencies: H1, A1
  - Verification method: Direct computation for sample integers

- **H3**: Norm preservation
  - Statement: For all n in Z, N(f(n)) = |n|^2.
  - Dependencies: H1, A1, T1
  - Verification method: Compute N(f(n)) for sample integers

## Reasoning Process

### Verifying Multiplicative Property (H2)

**Test case**: For n=2, m=3:
- f(2) = u_2
- f(3) = u_3
- f(6) = f(2*3) = u_2 * u_3 (left-normed)
- f(2)f(3) = u_2 * u_3

Thus f(6) = f(2)f(3) holds for this case.

### Verifying Norm Preservation (H3)

**Revised construction**: For each prime p, choose u_p in O such that N(u_p) = p. Then:
- f(n) = prod_{p}^{left} u_p^{a_p(n)}
- N(f(n)) = prod_{p} N(u_p)^{a_p(n)} = prod_{p} p^{a_p(n)} = n = |n|^2 for n > 0

This revised construction satisfies H3.
```

### 典型输出

```markdown
## Verification Results

### Claim 1: Multiplicative Property f(nm) = f(n)f(m)

**Check**: 
- Test case: n=2, m=3, nm=6
- Compute f(2), f(3), f(6) using the embedding map
- Verify f(6) = f(2)f(3)

**Result**: INCONCLUSIVE

**Notes**: 
- The reasoner's construction uses left-normed products: f(n) = prod_{p}^{left} u_p^{a_p(n)}
- Due to octonion non-associativity, (u_2 * u_3) may not equal the left-normed product u_2 * u_3 when computed in different orders
- Need explicit computation with chosen u_2, u_3 to verify
- Missing: Concrete values for u_2, u_3 in O

### Claim 2: Norm Preservation N(f(n)) = |n|^2

**Check**:
- For n > 0, verify N(f(n)) = n^2
- Use the revised construction: f(n) = prod_{p}^{left} u_p^{a_p(n)} where N(u_p) = p
- Compute N(f(n)) = prod_{p} N(u_p)^{a_p(n)} = prod_{p} p^{a_p(n)} = n

**Result**: VERIFIED (with assumption)

**Notes**:
- The reasoning is correct IF we can choose u_p such that N(u_p) = p for each prime p
- This requires existence of octonions with norm p, which is guaranteed by [dag:fact-001] (norm can take any non-negative real value)
- However, the equality N(f(n)) = n holds, not n^2. The hypothesis should be N(f(n)) = |n|, not |n|^2, OR the construction needs adjustment
- **Correction needed**: Either revise hypothesis to N(f(n)) = |n|, or scale construction by |n| to get N(f(n)) = |n|^2

### Claim 3: Embedding Map Well-Defined

**Check**:
- Verify f is well-defined for all integers
- Check f(1) = 1 (identity)
- Check f(-n) = -f(n) for n > 0

**Result**: VERIFIED

**Notes**:
- Construction explicitly defines f(1) = 1 and f(-n) = -f(n)
- Well-defined by prime factorization uniqueness [dag:fact-003]
```

---

## 6. Research Assistant [MODE:SUMMARY]

### System Prompt（最终发送给 LLM）

```text
You are the Scientific Research Assistant (single authority).

Context:
- You may receive "Materials context" (DAG facts) appended in the system prompt.
- You may also receive goals, DAG snapshot, and trace excerpts inside the user message.

Global rules:
- Be explicit about assumptions vs evidence.
- Keep outputs bounded (avoid long essays).
- NEVER include secrets or tool tokens.
- If the user asks to change/adjust the research plan, you MAY call create_plan (write Plan nodes to KnowledgeGraph).

Modes (the user message will include a mode marker):

0) [MODE:BRIEF]
   Output STRICT JSON ONLY (no markdown, no code fences).
   ...

1) [MODE:PLAN]
   Output STRICT JSON ONLY (no markdown, no code fences).
   ...

2) [MODE:SUMMARY]
   Output Markdown.
   Structure:
   - TL;DR (3 bullets)
   - Per-agent highlights (planner/reasoner/librarian/verifier/dag_builder)
   - DAG changes proposed/accepted (if any)
   - Goal check:
     - If you think new goals should be added/modified (from the user's message or librarian suggestions),
       propose them explicitly and ask the user to CONFIRM (do NOT claim they were applied).
   - Open questions / next actions
```

**注意**: SUMMARY 模式下**不包含** Materials Context。

### User Prompt

```text
[MODE:SUMMARY]
Question: How can we embed integers into the octonion algebra?

Plan: (see DAG plan nodes)

DAG facts written this round (ids):
- dag:fact-004
- dag:fact-005

DAG outcome:
- accepted mutationId=mutation-001 nodes=2 edges=3

Worker outputs (excerpts):
[planner]
## Research Plan: Integer Embedding into Octonions

### Hypotheses to Verify

1. **H1: Embedding Map Existence**
   - Statement: There exists an embedding map f: Z → O such that f(nm) = f(n)f(m) for all n, m in Z.
   - Dependencies: [dag:fact-001] (octonion algebra structure), [dag:fact-003] (prime factorization)
   - Verification method: Construct explicit map using prime factorization and octonion units

2. **H2: Multiplicative Property**
   - Statement: For all n, m in Z, f(nm) = f(n)f(m).
   - Dependencies: H1, [dag:fact-001]
   - Verification method: Direct computation for sample integers (e.g., f(2*3) = f(2)f(3))

3. **H3: Norm Preservation**
   - Statement: For all n in Z, N(f(n)) = |n|^2.
   - Dependencies: H1, [dag:fact-001] (norm definition)
   - Verification method: Compute N(f(n)) for sample integers and verify equality

[reasoner]
## Known Axioms

- **A1**: Octonion algebra definition [dag:fact-001]
  - Statement: O is the real octonion algebra with standard basis x = x_0 + sum_{i=1}^7 x_i e_i, conjugation bar{x} = x_0 - sum_{i=1}^7 x_i e_i, and norm N(x) = x bar{x} = bar{x} x in R_{>=0}.

## Hypotheses to Prove

- **H2**: Multiplicative property
  - Statement: For all n, m in Z, f(nm) = f(n)f(m).
  - Dependencies: H1, A1
  - Verification method: Direct computation for sample integers

- **H3**: Norm preservation
  - Statement: For all n in Z, N(f(n)) = |n|^2.
  - Dependencies: H1, A1, T1
  - Verification method: Compute N(f(n)) for sample integers

## Reasoning Process

### Constructing the Embedding Map (H1)

From A2, every positive integer n has a unique prime factorization n = prod_{p} p^{a_p(n)}.

From A1, we have the octonion algebra O with unit sphere S^7 = {u in O : N(u) = 1}.

**Construction**: For each prime p, choose a fixed unit octonion u_p in S^7. Then define:
- f(1) = 1 (multiplicative identity in O)
- For n > 0: f(n) = prod_{p}^{left} u_p^{a_p(n)} (left-normed product)
- For n < 0: f(n) = -f(-n)

### Verifying Norm Preservation (H3)

**Revised construction**: For each prime p, choose u_p in O such that N(u_p) = p. Then:
- f(n) = prod_{p}^{left} u_p^{a_p(n)}
- N(f(n)) = prod_{p} N(u_p)^{a_p(n)} = prod_{p} p^{a_p(n)} = n = |n|^2 for n > 0

[verifier]
## Verification Results

### Claim 1: Multiplicative Property f(nm) = f(n)f(m)

**Result**: INCONCLUSIVE

**Notes**: 
- Need explicit computation with chosen u_2, u_3 in O
- Missing: Concrete values for u_2, u_3 in O

### Claim 2: Norm Preservation N(f(n)) = |n|^2

**Result**: VERIFIED (with assumption)

**Notes**:
- The equality N(f(n)) = n holds, not n^2. The hypothesis should be N(f(n)) = |n|, not |n|^2, OR the construction needs adjustment
- **Correction needed**: Either revise hypothesis to N(f(n)) = |n|, or scale construction by |n| to get N(f(n)) = |n|^2
```

### 典型输出

```markdown
## TL;DR

- **Progress**: Defined embedding map f: Z → O using prime factorization and octonion units, verified norm preservation with correction needed
- **Key Finding**: Norm preservation requires N(f(n)) = |n| (not |n|^2) OR scaling adjustment
- **Next Step**: Resolve norm preservation hypothesis (N(f(n)) = |n| vs |n|^2) and verify multiplicative property with concrete octonion values

## Per-Agent Highlights

### Planner
- Produced clear execution plan with 3 hypotheses (H1: embedding existence, H2: multiplicative property, H3: norm preservation)
- Identified dependencies: [dag:fact-001] (octonion structure), [dag:fact-003] (prime factorization)
- Proposed test cases for verification (f(2*3), N(f(2)), etc.)

### Reasoner
- Constructed explicit embedding map: f(n) = prod_{p}^{left} u_p^{a_p(n)} where N(u_p) = p
- Identified key axioms: A1 (octonion definition), A2 (prime factorization)
- Revised construction to satisfy norm preservation: changed from unit octonions (N=1) to scaled octonions (N=p)
- **Issue**: Norm calculation yields N(f(n)) = n, not n^2, indicating hypothesis or construction needs adjustment

### Verifier
- Verified norm preservation with assumption (requires existence of u_p with N(u_p) = p)
- Found multiplicative property verification INCONCLUSIVE (needs concrete octonion values)
- Identified correction needed: hypothesis H3 should be N(f(n)) = |n| OR construction needs scaling by |n|

### DAG Builder
- Wrote 2 facts to DAG: embedding map definition, norm preservation verification result

## DAG Changes Proposed/Accepted

- **Accepted**: mutationId=mutation-001
  - Nodes: 2 new fact nodes (dag:fact-004, dag:fact-005)
  - Edges: 3 dependency edges
  - Content: Embedding map definition and norm preservation verification

## Goal Check

**Current Goals**:
1. ✅ Define explicit embedding map f: Z → O (COMPLETED)
2. ⚠️ Verify multiplicative property f(nm) = f(n)f(m) (INCONCLUSIVE - needs concrete values)
3. ⚠️ Verify norm preservation N(f(n)) = |n|^2 (VERIFIED with correction needed)

**Proposed Goal Modification**:
- **Goal 3** should be revised to: "Verify norm preservation N(f(n)) = |n|" OR "Adjust construction to achieve N(f(n)) = |n|^2"
- **Reason**: Current construction yields N(f(n)) = n, not n^2. Either the hypothesis is incorrect or the construction needs scaling.

**Action Required**: User confirmation needed for goal modification.

## Open Questions / Next Actions

1. **Resolve Norm Preservation Hypothesis**
   - Clarify whether target is N(f(n)) = |n| or |n|^2
   - If |n|^2 is required, adjust construction: f(n) = |n| * (prod_{p}^{left} u_p^{a_p(n)}) where N(u_p) = 1

2. **Complete Multiplicative Property Verification**
   - Choose concrete unit octonions u_2, u_3 in O
   - Compute f(2), f(3), f(6) explicitly
   - Verify f(6) = f(2)f(3) holds

3. **Extend Verification to More Test Cases**
   - Test f(4*5) = f(4)f(5)
   - Test f(12) = f(3*4) = f(3)f(4)
   - Verify for negative integers: f(-n) = -f(n)

4. **Consider Alternative Embedding Strategies**
   - Explore right-normed products vs left-normed
   - Consider embedding via E8 lattice structure [dag:fact-002]
```

---

## 总结

本文档展示了四个 Agent 在实际调用 LLM 时的完整流程：

1. **Research Assistant [BRIEF]**: 生成研究摘要（JSON）
2. **Research Assistant [PLAN]**: 生成执行计划（JSON）
3. **Planner**: 生成详细的研究计划和假设列表（Markdown）
4. **Reasoner**: 基于 Planner 的输出进行推理，列出已知公理、定理、定义和假设（Markdown）
5. **Verifier**: 验证 Reasoner 提出的关键声明（Markdown）
6. **Research Assistant [SUMMARY]**: 总结整个研究轮次的结果（Markdown）

每个 Agent 的输出都会传递给下一个 Agent 或用于最终总结，形成一个完整的研究工作流。

---

*最后更新: 2025-01-16*
