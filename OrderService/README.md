# פרויקט סיום: מעבר ממונוליט למיקרו-שירותים (Microservices Architecture)

פרויקט זה מציג ארכיטקטורה מתקדמת של מערכת מסחר אלקטרוני (E-Commerce) אשר פורקה מאפליקציה מונוליטית אחת ל-4 מיקרו-שירותים עצמאיים ומאובטחים, המנוהלים ומורצים באמצעות Docker Compose.

---

## 🏗️ מבנה הארכיטקטורה והרכיבים

1. **ApiGateway (YARP Reverse Proxy):** נקודת הכניסה היחידה למערכת (חשוף בפורט `8080`). מגן על השירותים הפנימיים ומנתב בקשות.
2. **ProductCatalogService:** ניהול קטלוג המוצרים.
3. **InventoryService:** ניהול מלאי המוצרים.
4. **OrderService:** ניהול ויצירת הזמנות.
5. **NotificationService:** שירות התראות אסינכרוני המבוסס על הודעות.
6. **RabbitMQ:** Message Broker המשמש לתקשורת אסינכרונית מבוססת אירועים (Event-Driven).

---

## 🔄 תזמור ותקשורת בין שירותים

- **תקשורת סינכרונית (HTTP Client):** בעת יצירת הזמנה, `OrderService` פונה ישירות ובאופן פנימי אל `InventoryService` כדי לשריין את המלאי.
- **תקשורת אסינכרונית (Event-Driven):** לאחר אישור המלאי, `OrderService` מפרסם אירוע (Event) לתוך תור ב-`RabbitMQ` בשם `order-notifications`. שירות ה-`NotificationService` מאזין לתור ברקע, צורך את ההודעה ומדפיס אותה ללוג.

---

## 🔒 אבטחה ובידוד רשת (Network Isolation)

כל המיקרו-שירותים חסומים לחלוטין בפני גישה ישירה מהעולם החיצון (נמחקו הגדרות ה-Ports שלהם מה-Compose). הגישה אליהם מתאפשרת אך ורק דרך ה-API Gateway בנתיבים הבאים:
- מוצרים: `GET http://localhost:8080/api/products`
- הזמנות: `POST http://localhost:8080/api/orders`
- מלאי: `GET http://localhost:8080/api/inventory`

---

## 🚀 הוראות הרצה (לבודקי העבודה)

כדי להריץ את כל הארכיטקטורה בלחיצת כפתור אחת, יש לוודא ש-Docker Desktop רץ ברקע, לפתוח את הטרמינל בתיקייה הראשית ולהריץ:

```bash
docker compose -p ecommerce up --build