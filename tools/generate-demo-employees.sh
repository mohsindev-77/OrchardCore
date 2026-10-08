#!/usr/bin/env bash
#
# Generates the two demo employee recipes from the headcount table in the prompt library's
# 7 October backlog note, as amended by the 10a ruling.
#
# Why a generator rather than two hand-written files: the figures are ~160 and ~70 people, and a
# hand-written file of that size is a file nobody re-reads — a wrong allocation or a missing
# secondary assignment would sit there unnoticed, which is exactly the failure the demo data
# exists to make visible. The rules are stated once here, in one place, and the output is checked
# in so that applying a recipe never depends on having run this.
#
# Re-run after changing the figures:  bash tools/generate-demo-employees.sh
#
set -euo pipefail

cd "$(dirname "$0")/.."

# A pool of names, English and Arabic halves paired. Cycled by index, so the output is the same
# every run and a diff is a change in the figures rather than in the shuffling.
GIVEN_EN=(Ahmed Fatima Bilal Ayesha Usman Hira Kashif Sana Imran Nida Tariq Rabia Zubair Mehwish Faisal Sadia Adeel Komal Nadeem Saba Rizwan Amna Shahid Iqra Waqar Maryam Junaid Hina Asif Noor Salman Zainab Arif Lubna Danish Farah Haris Sidra Owais Kiran)
GIVEN_AR=(أحمد فاطمة بلال عائشة عثمان هيرا كاشف سناء عمران نداء طارق رابعة زبير مهوش فيصل سعدية عديل كومل نديم صبا رضوان آمنة شاهد إقرا وقار مريم جنيد حنا آصف نور سلمان زينب عارف لبنى دانش فرح حارث سدرة أويس كرن)
FAMILY_EN=(Khan Malik Qureshi Siddiqui Chaudhry Butt Shaikh Ansari Hashmi Raza Javed Aslam Nawaz Bhatti Rehman Mirza Abbasi Durrani Yousafzai Gilani)
FAMILY_AR=(خان ملك قريشي صديقي چوهدري بٹ شيخ أنصاري هاشمي رضا جاويد أسلم نواز بھٹي رحمن ميرزا عباسي دراني يوسفزي جيلاني)

name_en() { echo "${GIVEN_EN[$(( $1 % ${#GIVEN_EN[@]} ))]} ${FAMILY_EN[$(( ($1 / 7) % ${#FAMILY_EN[@]} ))]}"; }
name_ar() { echo "${GIVEN_AR[$(( $1 % ${#GIVEN_AR[@]} ))]} ${FAMILY_AR[$(( ($1 / 7) % ${#FAMILY_AR[@]} ))]}"; }

SEQ=0
EMPLOYEES=""
ASSIGNMENTS=""

# add_person <code> <joinDate> <status> <statusFrom>
add_person() {
  local code=$1 joined=$2 status=$3 from=$4
  local en ar
  en=$(name_en "$SEQ"); ar=$(name_ar "$SEQ")
  SEQ=$(( SEQ + 1 ))

  [ -n "$EMPLOYEES" ] && EMPLOYEES="${EMPLOYEES},"
  EMPLOYEES="${EMPLOYEES}
        {
          \"code\": \"${code}\",
          \"nameEn\": \"${en}\",
          \"nameAr\": \"${ar}\",
          \"joinDate\": \"${joined}\",
          \"status\": \"${status}\",
          \"statusEffectiveFrom\": \"${from}\"
        }"
}

# add_assignment <employeeCode> <structureCode> <recordCode> <from> <percent> <isPrimary>
add_assignment() {
  [ -n "$ASSIGNMENTS" ] && ASSIGNMENTS="${ASSIGNMENTS},"
  ASSIGNMENTS="${ASSIGNMENTS}
        {
          \"employeeCode\": \"$1\",
          \"structureCode\": \"$2\",
          \"changes\": [
            {
              \"effectiveFrom\": \"$4\",
              \"split\": [ { \"recordCode\": \"$3\", \"allocationPercent\": $5, \"isPrimary\": $6 } ]
            }
          ]
        }"
}

# ---- Zenith ------------------------------------------------------------------------------
#
# The matrix this organisation exists to demonstrate: a department's figure is everyone whose HOME
# is that department, including the engineers deployed to a site team. Those engineers carry a
# primary assignment at their department and a secondary at the team, so cost rolls up by project
# and HR rolls up by department over the same people. Foremen, labour and technicians are
# project-hired and carry one primary at their team only — they belong to no department, and
# counting them in one would overstate every permanent headcount in the company.

ZENITH_JOIN=2024-01-15
ZENITH_HEADS=""

add_head() {
  [ -n "$ZENITH_HEADS" ] && ZENITH_HEADS="${ZENITH_HEADS},"
  ZENITH_HEADS="${ZENITH_HEADS}
        {
          \"structureCode\": \"$1\",
          \"recordCode\": \"$2\",
          \"terms\": [ $3 ]
        }"
}

term() { echo "{ \"employeeCode\": \"$1\", \"effectiveFrom\": \"$2\"$([ -n "${3:-}" ] && echo ", \"effectiveTo\": \"$3\"") }"; }

# Departments: the permanent staff, each with one primary assignment at home.
dept() {
  local prefix=$1 unit=$2 count=$3 i code
  for i in $(seq 1 "$count"); do
    code="${prefix}-$(printf %02d "$i")"
    add_person "$code" "$ZENITH_JOIN" Active "$ZENITH_JOIN"
    add_assignment "$code" zenith-org "$unit" "$ZENITH_JOIN" 100 true
  done
}

dept zenith-civil   zenith-dept-civil      14
dept zenith-elec    zenith-dept-electrical  8
dept zenith-mech    zenith-dept-mechanical  9
dept zenith-fin     zenith-dept-finance     5
dept zenith-hr      zenith-dept-hr          6

# The deployments: an engineer already counted by their department, now also on a team. The
# secondary is what attendance, site allowances and cost are recorded against.
deploy() {
  ASSIGNMENTS="${ASSIGNMENTS},
        {
          \"employeeCode\": \"$1\",
          \"structureCode\": \"zenith-org\",
          \"changes\": [
            {
              \"effectiveFrom\": \"$3\",
              \"split\": [
                { \"recordCode\": \"$4\", \"allocationPercent\": 60, \"isPrimary\": true },
                { \"recordCode\": \"$2\", \"allocationPercent\": 40, \"isPrimary\": false }
              ]
            }
          ]
        }"
}

# One site engineer on each site team, two electrical engineers on Electrical Works, one civil
# engineer on Civil Works — exactly the figures in the table, drawn from the departments above.
deploy zenith-civil-01 zenith-team-site-a          2024-03-01 zenith-dept-civil
deploy zenith-civil-02 zenith-team-site-b          2024-03-01 zenith-dept-civil
deploy zenith-civil-03 zenith-team-civil-works     2024-03-01 zenith-dept-civil
deploy zenith-elec-01  zenith-team-electrical-works 2024-03-01 zenith-dept-electrical
deploy zenith-elec-02  zenith-team-electrical-works 2024-03-01 zenith-dept-electrical

# The project-hired: one primary at their team and nothing else. Hired when the site opened, not
# when the company was founded, because that is what being project-hired means.
crew() {
  local prefix=$1 unit=$2 count=$3 joined=$4 i code
  for i in $(seq 1 "$count"); do
    code="${prefix}-$(printf %02d "$i")"
    add_person "$code" "$joined" Active "$joined"
    add_assignment "$code" zenith-org "$unit" "$joined" 100 true
  done
}

crew zenith-foreman-a zenith-team-site-a           3 2024-03-01
crew zenith-labour-a  zenith-team-site-a          40 2024-03-01
crew zenith-labour-b  zenith-team-site-b          30 2024-04-01
crew zenith-tech-ew   zenith-team-electrical-works 18 2024-04-01
crew zenith-labour-cw zenith-team-civil-works     25 2024-05-01

# Heads. Three shapes the engine has to be able to hold, each one in the demo on purpose:
#
#  - one person heading two units: the engineering manager over Civil and Mechanical, which an
#    allocation-based model could not express without charging them to both;
#  - an acting head who is not a member: Finance's manager covering HR, which is what ADR-0012
#    rejected the assignment-flag design for;
#  - a vacant post: Electrical's head stopped in October and nobody has taken over, which is a
#    fact about the organisation rather than missing data.
add_head zenith-org zenith-dept-civil      "$(term zenith-civil-01 2024-02-01)"
add_head zenith-org zenith-dept-mechanical "$(term zenith-civil-01 2024-02-01)"
add_head zenith-org zenith-dept-hr         "$(term zenith-fin-01 2024-02-01)"
add_head zenith-org zenith-dept-electrical "$(term zenith-elec-03 2024-02-01 2025-10-31)"
add_head zenith-org zenith-dept-finance    "$(term zenith-fin-02 2024-02-01)"
add_head zenith-org zenith-team-site-a     "$(term zenith-civil-01 2024-03-01)"

cat > src/WorkMate.Dimensions/Recipes/organisation-designer-zenith-employees.recipe.json <<JSON
{
  "name": "WorkMate.Demo.Zenith.Employees",
  "displayName": "WorkMate demo: Zenith Engineering & Construction — employees",
  "description": "TEST DATA — the people on the Zenith organisation, at the headcounts in the prompt library's 7 October backlog note. Apply organisation-designer-zenith.recipe.json first: every assignment and appointment here names a unit by a code that recipe creates.",
  "author": "Aramis Enterprise Solutions",
  "version": "0.1.0",
  "issetuprecipe": false,
  "categories": ["workmate", "demo"],
  "tags": ["workmate", "demo", "test-data", "employees"],

  "\$notes": [
    "Separate from the structure recipe on purpose. A test about the shape of an organisation should not have to load 160 people to run, and the browser suite should not pay for them on every tenant it builds. Applying this one needs the structure one; the reverse is not true.",
    "The matrix: engineers on a site team carry a primary at their home department and a secondary at the team, so cost rolls up by project and HR rolls up by department over the same rows. Foremen, labour and technicians are project-hired and carry one primary at their team only.",
    "Heads include one person over two units, an acting head who is not a member of the unit, and a post left vacant — the three shapes ADR-0012 was decided for.",
    "Generated by tools/generate-demo-employees.sh. Edit the figures there, not here."
  ],

  "steps": [
    {
      "name": "employees",
      "employees": [${EMPLOYEES}
      ]
    },
    {
      "name": "employee-assignments",
      "assignments": [${ASSIGNMENTS}
      ]
    },
    {
      "name": "unit-heads",
      "heads": [${ZENITH_HEADS}
      ]
    }
  ]
}
JSON

# ---- Crescent ----------------------------------------------------------------------------
#
# A branch network rather than a matrix: everybody has one assignment. The shape worth having here
# is the branch that is itself a leaf — Gujranwala, Sialkot and Hyderabad have no departments, so
# their staff attach to the branch — alongside the two big branches that do.

SEQ=500
EMPLOYEES=""
ASSIGNMENTS=""
CRESCENT_HEADS=""

add_crescent_head() {
  [ -n "$CRESCENT_HEADS" ] && CRESCENT_HEADS="${CRESCENT_HEADS},"
  CRESCENT_HEADS="${CRESCENT_HEADS}
        {
          \"structureCode\": \"crescent-org\",
          \"recordCode\": \"$1\",
          \"terms\": [ $2 ]
        }"
}

CRESCENT_JOIN=2024-02-01

unit() {
  local prefix=$1 record=$2 count=$3 i code
  for i in $(seq 1 "$count"); do
    code="${prefix}-$(printf %02d "$i")"
    add_person "$code" "$CRESCENT_JOIN" Active "$CRESCENT_JOIN"
    add_assignment "$code" crescent-org "$record" "$CRESCENT_JOIN" 100 true
  done
}

unit crescent-cr   crescent-dept-credit-risk     6
unit crescent-tr   crescent-dept-treasury        3
unit crescent-hr   crescent-dept-hr              4
unit crescent-lhrc crescent-dept-lhr-credit     12
unit crescent-lhro crescent-dept-lhr-operations  9
unit crescent-khic crescent-dept-khi-credit     10
unit crescent-khio crescent-dept-khi-operations  7

# The branches that are leaves: a manager and their staff, all on the branch itself. The manager
# is both a member and the head, which is the ordinary case — stated here so that the two unusual
# cases below read as the exceptions they are.
unit crescent-guj crescent-branch-gujranwala 7
unit crescent-sia crescent-branch-sialkot    5
unit crescent-hyd crescent-branch-hyderabad  6

add_crescent_head crescent-branch-gujranwala "$(term crescent-guj-01 "$CRESCENT_JOIN")"
add_crescent_head crescent-branch-sialkot    "$(term crescent-sia-01 "$CRESCENT_JOIN")"
add_crescent_head crescent-branch-hyderabad  "$(term crescent-hyd-01 "$CRESCENT_JOIN")"
add_crescent_head crescent-dept-credit-risk  "$(term crescent-cr-01 "$CRESCENT_JOIN")"
add_crescent_head crescent-dept-hr           "$(term crescent-hr-01 "$CRESCENT_JOIN")"

# An acting head who is not a member: Credit Risk's second officer covering Karachi — Operations,
# from head office, which is what covering a branch actually looks like.
add_crescent_head crescent-dept-khi-operations "$(term crescent-cr-02 2025-01-01)"

# And a vacant post, by a term that ended with no successor: Treasury has had nobody since June.
add_crescent_head crescent-dept-treasury "$(term crescent-tr-01 "$CRESCENT_JOIN" 2025-06-30)"

cat > src/WorkMate.Dimensions/Recipes/organisation-designer-crescent-employees.recipe.json <<JSON
{
  "name": "WorkMate.Demo.Crescent.Employees",
  "displayName": "WorkMate demo: Crescent Microfinance Bank — employees",
  "description": "TEST DATA — the people on the Crescent organisation, at the headcounts in the prompt library's 7 October backlog note. Apply organisation-designer-crescent.recipe.json first: every assignment and appointment here names a unit by a code that recipe creates.",
  "author": "Aramis Enterprise Solutions",
  "version": "0.1.0",
  "issetuprecipe": false,
  "categories": ["workmate", "demo"],
  "tags": ["workmate", "demo", "test-data", "employees"],

  "\$notes": [
    "Separate from the structure recipe on purpose, for the reason the Zenith one is: a test about the shape of an organisation should not have to load its people to run.",
    "No matrix here — a branch network is one assignment each. What this one carries that Zenith does not is the branch that is itself a leaf: Gujranwala, Sialkot and Hyderabad have no departments, so their staff attach to the branch record directly.",
    "Heads include a branch manager who is a member of the branch they lead, an acting head covering a branch department from head office, and a post vacant since June.",
    "Generated by tools/generate-demo-employees.sh. Edit the figures there, not here."
  ],

  "steps": [
    {
      "name": "employees",
      "employees": [${EMPLOYEES}
      ]
    },
    {
      "name": "employee-assignments",
      "assignments": [${ASSIGNMENTS}
      ]
    },
    {
      "name": "unit-heads",
      "heads": [${CRESCENT_HEADS}
      ]
    }
  ]
}
JSON

echo "Wrote:"
echo "  src/WorkMate.Dimensions/Recipes/organisation-designer-zenith-employees.recipe.json"
echo "  src/WorkMate.Dimensions/Recipes/organisation-designer-crescent-employees.recipe.json"
