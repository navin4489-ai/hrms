import 'common.dart';

/// `GET /performance/cycles/current` response (nullable).
class ReviewCycle {
  const ReviewCycle({
    required this.id,
    required this.name,
    this.startDate,
    this.endDate,
    this.status,
    this.selfAssessmentStatus,
    this.selfAssessmentDueDate,
    this.selfAssessmentSubmittedAt,
  });

  factory ReviewCycle.fromJson(Map<String, dynamic> json) {
    final sa = json['selfAssessment'];
    return ReviewCycle(
      id: JsonX.strOr(json['id']),
      name: JsonX.strOr(json['name']),
      startDate: JsonX.str(json['startDate']),
      endDate: JsonX.str(json['endDate']),
      status: JsonX.str(json['status']),
      selfAssessmentStatus: sa is Map
          ? JsonX.str(sa['status'])
          : JsonX.str(json['selfAssessmentStatus']),
      selfAssessmentDueDate: sa is Map
          ? JsonX.str(sa['dueDate'])
          : JsonX.str(json['selfAssessmentDueDate']),
      selfAssessmentSubmittedAt: sa is Map
          ? JsonX.str(sa['submittedAt'])
          : null,
    );
  }

  final String id;
  final String name;
  final String? startDate;
  final String? endDate;
  final String? status;
  final String? selfAssessmentStatus;
  final String? selfAssessmentDueDate;
  final String? selfAssessmentSubmittedAt;

  bool get isOpen => status == 'Open';
  bool get canSubmitAssessment => isOpen && selfAssessmentStatus != 'Submitted';
}

/// `GET /performance/goals` item.
class Goal {
  const Goal({
    required this.id,
    required this.title,
    this.description,
    this.category,
    this.weight,
    this.target,
    this.progress = 0,
    this.status,
    this.managerFeedback,
  });

  factory Goal.fromJson(Map<String, dynamic> json) => Goal(
    id: JsonX.strOr(json['id']),
    title: JsonX.strOr(json['title']),
    description: JsonX.str(json['description']),
    category: JsonX.str(json['category']),
    weight: JsonX.doubleOrNull(json['weight']),
    target: JsonX.str(json['target']),
    progress: (JsonX.numOrNull(json['progress']) ?? 0).clamp(0, 100).toInt(),
    status: JsonX.str(json['status']),
    managerFeedback: JsonX.str(json['managerFeedback']),
  );

  final String id;
  final String title;
  final String? description;
  final String? category;
  final double? weight;
  final String? target;
  final int progress;
  final String? status;
  final String? managerFeedback;
}

/// `GET /performance/reviews` item.
class PerformanceReview {
  const PerformanceReview({
    required this.id,
    required this.cycleName,
    this.publishedAt,
    this.rating,
    this.summary,
    this.acknowledgedAt,
  });

  factory PerformanceReview.fromJson(Map<String, dynamic> json) =>
      PerformanceReview(
        id: JsonX.strOr(json['id']),
        cycleName: JsonX.strOr(json['cycleName']),
        publishedAt: JsonX.str(json['publishedAt']),
        rating: JsonX.doubleOrNull(json['rating']),
        summary: JsonX.str(json['summary']),
        acknowledgedAt: JsonX.str(json['acknowledgedAt']),
      );

  final String id;
  final String cycleName;
  final String? publishedAt;
  final double? rating;
  final String? summary;
  final String? acknowledgedAt;

  bool get needsAcknowledgement => acknowledgedAt == null;
}

/// `GET /training/courses` item.
class Course {
  const Course({
    required this.id,
    required this.title,
    this.category,
    this.durationHours,
    this.mandatory = false,
    this.dueDate,
    this.enrollmentStatus = 'NotEnrolled',
    this.completedAt,
    this.certificateUrl,
  });

  factory Course.fromJson(Map<String, dynamic> json) => Course(
    id: JsonX.strOr(json['id']),
    title: JsonX.strOr(json['title']),
    category: JsonX.str(json['category']),
    durationHours: JsonX.doubleOrNull(json['durationHours']),
    mandatory: JsonX.boolOr(json['mandatory']),
    dueDate: JsonX.str(json['dueDate']),
    enrollmentStatus: JsonX.strOr(json['enrollmentStatus'], 'NotEnrolled'),
    completedAt: JsonX.str(json['completedAt']),
    certificateUrl: JsonX.str(json['certificateUrl']),
  );

  final String id;
  final String title;
  final String? category;
  final double? durationHours;
  final bool mandatory;
  final String? dueDate;
  final String enrollmentStatus;
  final String? completedAt;
  final String? certificateUrl;

  bool get canEnroll => enrollmentStatus == 'NotEnrolled';

  /// Optional courses only — mandatory completions need `training.manage`.
  bool get canComplete => !mandatory && enrollmentStatus == 'Enrolled';
}
