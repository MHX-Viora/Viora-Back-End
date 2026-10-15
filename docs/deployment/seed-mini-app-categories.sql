-- Add missing default Mini App categories; preserve categories edited by admins.
BEGIN;

INSERT INTO "MiniAppCategories"
    ("Id", "Name", "Slug", "Icon", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt")
VALUES
    ('22222222-2222-4222-8222-222222222201', 'Tiện ích', 'tien-ich', 'apps-outline', 10, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222202', 'Mua sắm', 'mua-sam', 'cart-outline', 20, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222203', 'Ăn uống', 'an-uong', 'restaurant-outline', 30, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222204', 'Du lịch', 'du-lich', 'airplane-outline', 40, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222205', 'Giáo dục', 'giao-duc', 'school-outline', 50, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222206', 'Giải trí', 'giai-tri', 'game-controller-outline', 60, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222207', 'Sức khỏe', 'suc-khoe', 'fitness-outline', 70, TRUE, NOW(), NOW()),
    ('22222222-2222-4222-8222-222222222208', 'Dịch vụ', 'dich-vu', 'briefcase-outline', 80, TRUE, NOW(), NOW())
ON CONFLICT DO NOTHING;

COMMIT;
