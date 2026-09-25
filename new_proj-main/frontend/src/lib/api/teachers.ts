import { apiFetch, apiFetchBlob } from "../api-client";

export interface HeadlineChip { title: string; score: string | null }

export interface Teacher {
  id: string; fullName: string; email: string; status: string;
  /** Whether the caller may open this profile — a teacher sees colleagues in the list but opens only their own page. */
  canOpen: boolean;
  /** From the portfolio; only present for a caller who may read portfolios, on rows they may open. */
  specialization?: string | null;
  /** The confirmed exam scores (newest three), same visibility as `specialization`. */
  headline?: HeadlineChip[] | null;
}
export interface TeacherOverview {
  userId: string; fullName: string; email: string; totalStudents: number;
  /** Changes with every new picture; null when there is none. */
  photoVersion?: string | null;
  groups: { groupId: string; groupName: string; role: "MainTeacher" | "SupportTeacher"; studentCount: number }[];
  students: { studentId: string; fullName: string; studentNumber: string; groupId: string; groupName: string }[];
}
export interface TeacherPerformance {
  activeStudents: number;
  groupsCount: number;
  attendanceRate: number;
  homeworkCompletionRate: number;
  averageExamScore: number | null;
}

export interface TeacherSummary {
  userId: string; fullName: string; email: string; phone: string; status: string;
  branches: string[]; joinedAt: string; groupsCount: number; activeStudents: number;
  specialization?: string | null; headline?: HeadlineChip[] | null;
}

export interface WorkloadGroupAttendance {
  groupId: string; groupName: string; lessonsHeld: number; lessonsCancelled: number;
  attended: number; counted: number; attendanceRate: number;
}
export interface WorkloadGroupHomework {
  groupId: string; groupName: string; created: number; pendingReview: number; averageReviewHours: number | null;
}
export interface WorkloadMonth { month: string; average: number; results: number }
export interface WorkloadGroupExams {
  groupId: string; groupName: string; exams: number; results: number; averageOverall: number | null; trend: WorkloadMonth[];
}
/** Percentages are 0–100 (as on the Performance tab). */
export interface TeacherWorkload {
  from: string; to: string;
  lessonsHeld: number; lessonsCancelled: number; attendanceRate: number;
  homeworkCreated: number; homeworkPendingReview: number; averageReviewHours: number | null;
  attendance: WorkloadGroupAttendance[]; homework: WorkloadGroupHomework[]; exams: WorkloadGroupExams[];
}

export interface StaffLink { userId: string; fullName: string; groupId: string; groupName: string }
export interface TeacherSupport { supportTeachers: StaffLink[]; assistsIn: StaffLink[] }

export const listTeachers = () => apiFetch<Teacher[]>("/api/teachers");
export const getTeacher = (id: string) => apiFetch<TeacherOverview>(`/api/teachers/${id}`);
export const getTeacherPerformance = (id: string) => apiFetch<TeacherPerformance>(`/api/teachers/${id}/performance`);
export const getTeacherSummary = (id: string) => apiFetch<TeacherSummary>(`/api/teachers/${id}/summary`);
export const getTeacherSupport = (id: string) => apiFetch<TeacherSupport>(`/api/teachers/${id}/support-teachers`);
export const getTeacherWorkload = (id: string, range: { from: string; to: string }) =>
  apiFetch<TeacherWorkload>(`/api/teachers/${id}/workload?from=${range.from}&to=${range.to}`);

// ── Portfolio (S05.02) ──────────────────────────────────────────────────────────────────────────

export type CredentialType = "ExamScore" | "Certificate" | "Degree" | "Course" | "Award" | "Other";

export const CREDENTIAL_TYPES: { value: CredentialType; label: string }[] = [
  { value: "ExamScore", label: "Imtihon balli (IELTS, TOEFL, CEFR…)" },
  { value: "Certificate", label: "Sertifikat (CELTA, TESOL…)" },
  { value: "Degree", label: "Diplom / daraja" },
  { value: "Course", label: "Kurs" },
  { value: "Award", label: "Mukofot" },
  { value: "Other", label: "Boshqa" },
];

export const CREDENTIAL_FILE_ACCEPT = ".pdf,.png,.jpg,.jpeg,.webp";
export const CREDENTIAL_FILE_MAX_BYTES = 10 * 1024 * 1024;

export interface PortfolioProfile {
  bio: string | null; specialization: string | null; yearsOfExperience: number | null;
  education: string | null; languages: string | null; teachingSince: string | null;
}

export interface Credential {
  id: string; type: CredentialType; title: string; issuer: string | null; score: string | null;
  issuedOn: string | null; expiresOn: string | null; reference: string | null; note: string | null;
  file: { fileName: string; contentType: string; size: number } | null;
  isVerified: boolean; verifiedByName: string | null; verifiedAt: string | null; isExpired: boolean; createdAt: string;
}

/** `verificationReset`: the change took away a confirmation the credential had. */
export interface CredentialResult { credential: Credential; verificationReset: boolean }

export interface Experience {
  id: string; employer: string; position: string; fromYear: number; toYear: number | null; description: string | null;
}

export interface TeacherPortfolio {
  profile: PortfolioProfile; credentials: Credential[]; experience: Experience[]; headline: HeadlineChip[];
}

export interface CredentialInput {
  type: CredentialType; title: string; issuer?: string; score?: string; issuedOn?: string; expiresOn?: string;
  reference?: string; note?: string;
}

export interface ExperienceInput { employer: string; position: string; fromYear: number; toYear?: number | null; description?: string }

const portfolioUrl = (teacherId: string, path = "") => `/api/teachers/${teacherId}${path}`;
const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) });

export const getTeacherPortfolio = (teacherId: string) => apiFetch<TeacherPortfolio>(portfolioUrl(teacherId, "/portfolio"));
export const saveTeacherProfile = (teacherId: string, profile: Partial<PortfolioProfile>) =>
  apiFetch<PortfolioProfile>(portfolioUrl(teacherId, "/profile"), json("PUT", profile));

export const addCredential = (teacherId: string, input: CredentialInput) =>
  apiFetch<CredentialResult>(portfolioUrl(teacherId, "/credentials"), json("POST", input));
export const updateCredential = (teacherId: string, id: string, input: CredentialInput) =>
  apiFetch<CredentialResult>(portfolioUrl(teacherId, `/credentials/${id}`), json("PATCH", input));
export const deleteCredential = (teacherId: string, id: string) =>
  apiFetch<void>(portfolioUrl(teacherId, `/credentials/${id}`), { method: "DELETE" });

export function uploadCredentialFile(teacherId: string, id: string, file: File) {
  const form = new FormData();
  form.set("file", file);
  return apiFetch<CredentialResult>(portfolioUrl(teacherId, `/credentials/${id}/file`), { method: "POST", body: form });
}
export const deleteCredentialFile = (teacherId: string, id: string) =>
  apiFetch<CredentialResult>(portfolioUrl(teacherId, `/credentials/${id}/file`), { method: "DELETE" });
/** The scan sits behind the bearer token, so it is fetched as a blob rather than linked. */
export const fetchCredentialFile = (teacherId: string, id: string) =>
  apiFetchBlob(portfolioUrl(teacherId, `/credentials/${id}/file`));

export const verifyCredential = (teacherId: string, id: string) =>
  apiFetch<Credential>(portfolioUrl(teacherId, `/credentials/${id}/verify`), { method: "POST" });
export const unverifyCredential = (teacherId: string, id: string) =>
  apiFetch<Credential>(portfolioUrl(teacherId, `/credentials/${id}/unverify`), { method: "POST" });

export const addExperience = (teacherId: string, input: ExperienceInput) =>
  apiFetch<Experience>(portfolioUrl(teacherId, "/experience"), json("POST", input));
export const updateExperience = (teacherId: string, id: string, input: ExperienceInput) =>
  apiFetch<Experience>(portfolioUrl(teacherId, `/experience/${id}`), json("PATCH", input));
export const deleteExperience = (teacherId: string, id: string) =>
  apiFetch<void>(portfolioUrl(teacherId, `/experience/${id}`), { method: "DELETE" });

/** A confirmed credential as a student or parent sees it — no reference, note, scan or verifier. */
export interface PublicCredential {
  id: string; type: CredentialType; title: string; issuer: string | null; score: string | null;
  issuedOn: string | null; expiresOn: string | null; isExpired: boolean;
  /** Null when there is no scan; otherwise the type (image or PDF) the viewer needs. */
  fileContentType: string | null;
}

export interface PublicPortfolio {
  userId: string; fullName: string; photoVersion: string | null; profile: PortfolioProfile;
  credentials: PublicCredential[]; experience: Experience[]; headline: HeadlineChip[];
}

/** "Mening o'qituvchim": the confirmed part of the portfolio of a teacher of the caller's own group (or their child's). */
export const getPublicPortfolio = (teacherId: string) => apiFetch<PublicPortfolio>(portfolioUrl(teacherId, "/portfolio/public"));
/** The scan of a confirmed credential, for the viewer — fetched with the bearer token. */
export const fetchPublicCredentialFile = (teacherId: string, credentialId: string) =>
  apiFetchBlob(portfolioUrl(teacherId, `/portfolio/public/credentials/${credentialId}/file`));
